using System.ComponentModel.DataAnnotations;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.NodeSessions;

namespace NodeService.WebServer.Services.Tasks;

public class TaskActivationService : BackgroundService
{
    readonly ExceptionCounter _exceptionCounter;
    readonly BatchQueue<FireTaskParameters> _fireTaskBatchQueue;
    readonly ILogger<TaskActivationService> _logger;
    readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepositoryFactory;
    readonly INodeSessionService _nodeSessionService;
    readonly PriorityQueue<TaskPenddingContext, TaskExecutionPriority> _priorityQueue;
    readonly BatchQueue<TaskCancellationParameters> _taskCancellationQueue;
    readonly ITaskPenddingContextManager _taskPenddingContextManager;
    readonly ApplicationRepositoryFactory<TaskDefinitionModel> _taskDefinitionRepositoryFactory;
    readonly ApplicationRepositoryFactory<TaskExecutionInstanceModel> _taskExecutionInstanceRepositoryFactory;
    readonly BatchQueue<TaskExecutionReportMessage> _taskExecutionReportBatchQueue;
    readonly ApplicationRepositoryFactory<TaskActivationRecordModel> _taskActivationRecordRepositoryFactory;
    readonly ApplicationRepositoryFactory<TaskTypeDescConfigModel> _taskTypeDescConfigRepositoryFactory;

    public TaskActivationService(
        ILogger<TaskActivationService> logger,
        ApplicationRepositoryFactory<TaskDefinitionModel> taskDefinitionRepositoryFactory,
        ApplicationRepositoryFactory<TaskExecutionInstanceModel> taskExecutionInstanceRepositoryFactory,
        ApplicationRepositoryFactory<TaskActivationRecordModel> jobFireConfigRepositoryFactory,
        ApplicationRepositoryFactory<TaskTypeDescConfigModel> taskTypeDescConfigRepositoryFactory,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepositoryFactory,
        BatchQueue<TaskExecutionReportMessage> taskExecutionReportBatchQueue,
        INodeSessionService nodeSessionService,
        ExceptionCounter exceptionCounter,
        BatchQueue<FireTaskParameters> fireTaskBatchQueue,
        BatchQueue<TaskCancellationParameters> taskCancellationQueue,
        ITaskPenddingContextManager taskPenddingContextManager)
    {
        _logger = logger;
        PenddingContextChannel = Channel.CreateUnbounded<TaskPenddingContext>();
        _priorityQueue = new PriorityQueue<TaskPenddingContext, TaskExecutionPriority>();
        _nodeSessionService = nodeSessionService;
        _taskDefinitionRepositoryFactory = taskDefinitionRepositoryFactory;
        _taskExecutionInstanceRepositoryFactory = taskExecutionInstanceRepositoryFactory;
        _taskActivationRecordRepositoryFactory = jobFireConfigRepositoryFactory;
        _taskTypeDescConfigRepositoryFactory = taskTypeDescConfigRepositoryFactory;
        _nodeInfoRepositoryFactory = nodeInfoRepositoryFactory;

        _taskExecutionReportBatchQueue = taskExecutionReportBatchQueue;
        _exceptionCounter = exceptionCounter;
        _fireTaskBatchQueue = fireTaskBatchQueue;
        _taskCancellationQueue = taskCancellationQueue;
        _taskPenddingContextManager = taskPenddingContextManager;
        PenddingActionBlock = new ActionBlock<TaskPenddingContext>(ProcessPenddingContextAsync,
        new ExecutionDataflowBlockOptions
        {
            EnsureOrdered = true,
            MaxDegreeOfParallelism = Environment.ProcessorCount / 2
        });
    }

    public Channel<TaskPenddingContext> PenddingContextChannel { get; }
    public ActionBlock<TaskPenddingContext> PenddingActionBlock { get; }

    private async Task SchedulePenddingContextAsync(object? state)
    {
        if (state is not CancellationToken cancellationToken) return;
        while (true)
        {
            await PenddingContextChannel.Reader.WaitToReadAsync(cancellationToken);
            while (PenddingContextChannel.Reader.TryRead(out var penddingContext))
                _priorityQueue.Enqueue(penddingContext, penddingContext.TaskDefinition.Priority);
            while (_priorityQueue.Count > 0)
            {
                while (PenddingActionBlock.InputCount < 128
                   &&
                   _priorityQueue.TryDequeue(out var penddingContext, out var _))
                {
                    PenddingActionBlock.Post(penddingContext);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
            }
        }
    }

    private async Task ProcessPenddingContextAsync(TaskPenddingContext context)
    {
        var readyToRun = false;
        try
        {
            using var repo = _taskExecutionInstanceRepositoryFactory.CreateRepository();
            _logger.LogInformation($"{context.Id}:Start init");
            await context.EnsureInitAsync();
            switch (context.TaskDefinition.ExecutionStrategy)
            {
                case TaskExecutionStrategy.Concurrent:
                    readyToRun = true;
                    break;
                case TaskExecutionStrategy.Queue:
                    readyToRun = await context.WaitForRunningTasksAsync(repo);
                    break;
                case TaskExecutionStrategy.Stop:
                    readyToRun = await context.StopRunningTasksAsync(repo);
                    break;
                case TaskExecutionStrategy.Skip:
                    return;
            }

            if (readyToRun)
            {
                while (!context.CancellationToken.IsCancellationRequested)
                {
                    if (context.NodeSessionService.GetNodeStatus(context.NodeSessionId) == NodeStatus.Online) break;
                    await Task.Delay(TimeSpan.FromSeconds(1), context.CancellationToken);
                }

                var rsp = await _nodeSessionService.SendTaskExecutionEventAsync(
                    context.NodeSessionId,
                    context.TriggerEvent,
                    context.CancellationToken);
                _logger.LogInformation($"{context.Id}:SendTaskExecutionEventAsync");
                await _taskExecutionReportBatchQueue.SendAsync(new TaskExecutionReportMessage
                {
                    Message = new TaskExecutionReport
                    {
                        Id = context.Id,
                        Status = TaskExecutionStatus.Triggered,
                        Message = rsp.Message
                    }
                });
                _logger.LogInformation($"{context.Id}:SendAsync Triggered");
                return;
            }
        }
        catch (TaskCanceledException ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError($"{context.Id}:{ex}");
        }
        catch (OperationCanceledException ex)
        {
            if (ex.CancellationToken == context.CancellationToken)
            {

            }
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError($"{context.Id}:{ex}");
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }

        _taskPenddingContextManager.RemoveContext(context.Id, out _);
        if (context.CancellationToken.IsCancellationRequested)
        {
            await _taskExecutionReportBatchQueue.SendAsync(new TaskExecutionReportMessage
            {
                Message = new TaskExecutionReport
                {
                    Id = context.Id,
                    Status = TaskExecutionStatus.PenddingTimeout,
                    Message = "Timeout"
                }
            });
            _logger.LogInformation($"{context.Id}:SendAsync PenddingTimeout");
        }
    }

    private async Task FireTaskAsync(
        IRepository<TaskDefinitionModel> taskDefinitionRepo,
        IRepository<TaskExecutionInstanceModel> taskExecutionInstanceRepo,
        IRepository<TaskActivationRecordModel> taskActivationRecordRepo,
        IRepository<TaskTypeDescConfigModel> taskTypeDescConfigRepo,
        IRepository<NodeInfoModel> nodeInfoRepo,
        FireTaskParameters parameters,
        TaskDefinitionModel taskDefinition,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var nodeIdListFilter = DataFilterCollection<string>.Includes(taskDefinition.NodeList.Select(x => x.Value));
            var nodeInfoList = await nodeInfoRepo.ListAsync(
                new NodeInfoSpecification(
                    AreaTags.Any,
                    NodeStatus.All,
                    NodeDeviceType.Computer,
                    nodeIdListFilter),
                cancellationToken);
            foreach (var nodeEntry in taskDefinition.NodeList)
                try
                {
                    if (string.IsNullOrEmpty(nodeEntry.Value))
                    {
                        continue;
                    }
                    NodeId nodeId = NodeId.Null;
                    foreach (var nodeInfo in nodeInfoList)
                    {
                        if (nodeInfo.Id == nodeEntry.Value)
                        {
                            nodeId = new NodeId(nodeEntry.Value);
                            break;
                        }
                    }
                    if (nodeId.IsNullOrEmpty) continue;
                    foreach (var nodeSessionId in _nodeSessionService.EnumNodeSessions(nodeId))
                    {
                        var taskExecutionInstance = await AddTaskExecutionInstanceAsync(
                            taskDefinition,
                            taskExecutionInstanceRepo,
                            nodeSessionId,
                            parameters,
                            cancellationToken);
                        var context = new TaskPenddingContext(taskExecutionInstance.Id)
                        {
                            NodeSessionService = _nodeSessionService,
                            NodeSessionId = nodeSessionId,
                            TriggerEvent = taskExecutionInstance.ToTriggerEvent(taskDefinition, parameters.EnvironmentVariables),
                            FireParameters = parameters,
                            TaskDefinition = taskDefinition
                        };
                       
                        if (_taskPenddingContextManager.AddContext(context))
                        {
                            await PenddingContextChannel.Writer.WriteAsync(context, cancellationToken);
                        }

                    }
                }
                catch (Exception ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }

            _logger.LogInformation($"Job initialized {parameters.FireInstanceId}");
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    public async Task<TaskExecutionInstanceModel> AddTaskExecutionInstanceAsync(
        TaskDefinitionModel taskDefinition,
        IRepository<TaskExecutionInstanceModel> taskExecutionInstanceRepo,
        NodeSessionId nodeSessionId,
        FireTaskParameters parameters,
        CancellationToken cancellationToken = default)
    {
        var nodeName = _nodeSessionService.GetNodeName(nodeSessionId);
        var taskExecutionInstance = new TaskExecutionInstanceModel
        {
            Id = Guid.NewGuid().ToString(),
            Name = $"{nodeName} {taskDefinition.Name} {parameters.FireInstanceId}",
            NodeInfoId = nodeSessionId.NodeId.Value,
            Status = TaskExecutionStatus.Triggered,
            FireTimeUtc = parameters.FireTimeUtc.DateTime,
            Message = string.Empty,
            FireType = "Server",
            TriggerSource = parameters.TriggerSource,
            JobScheduleConfigId = taskDefinition.Id,
            ParentId = parameters.ParentTaskId,
            FireInstanceId = parameters.FireInstanceId
        };


        var isOnline = _nodeSessionService.GetNodeStatus(nodeSessionId) == NodeStatus.Online;
        if (isOnline)
        {
            switch (taskDefinition.ExecutionStrategy)
            {
                case TaskExecutionStrategy.Concurrent:
                    taskExecutionInstance.Message = $"{nodeName}:triggered";
                    break;
                case TaskExecutionStrategy.Queue:
                    taskExecutionInstance.Status = TaskExecutionStatus.Pendding;
                    taskExecutionInstance.Message = $"{nodeName}: waiting for any job";
                    break;
                case TaskExecutionStrategy.Skip:
                    taskExecutionInstance.Status = TaskExecutionStatus.Pendding;
                    taskExecutionInstance.Message = $"{nodeName}: start job";
                    break;
                case TaskExecutionStrategy.Stop:
                    taskExecutionInstance.Status = TaskExecutionStatus.Pendding;
                    taskExecutionInstance.Message = $"{nodeName}: waiting for kill all job";
                    break;
            }
        }
        else
        {
            taskExecutionInstance.Message = $"{nodeName} offline";
            taskExecutionInstance.Status = TaskExecutionStatus.Failed;
        }

        if (parameters.NextFireTimeUtc != null)
            taskExecutionInstance.NextFireTimeUtc = parameters.NextFireTimeUtc.Value.UtcDateTime;
        if (parameters.PreviousFireTimeUtc != null)
            taskExecutionInstance.NextFireTimeUtc = parameters.PreviousFireTimeUtc.Value.UtcDateTime;
        if (parameters.ScheduledFireTimeUtc != null)
            taskExecutionInstance.ScheduledFireTimeUtc = parameters.ScheduledFireTimeUtc.Value.UtcDateTime;


        await taskExecutionInstanceRepo.AddAsync(taskExecutionInstance, cancellationToken);

        return taskExecutionInstance;
    }

    private (string TaskDefinitionId, string TaskFireInstanceId) TaskParametersGroupFunc(
        FireTaskParameters fireTaskParameters)
    {
        return (fireTaskParameters.TaskDefinitionId, fireTaskParameters.FireInstanceId);
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _ = Task.Factory.StartNew(SchedulePenddingContextAsync, cancellationToken, cancellationToken);
        await foreach (var arrayPoolCollection in _fireTaskBatchQueue.ReceiveAllAsync(cancellationToken))
            try
            {
                using var taskExecutionInstanceRepo = _taskExecutionInstanceRepositoryFactory.CreateRepository();
                using var taskActivationRecordRepo = _taskActivationRecordRepositoryFactory.CreateRepository();
                using var taskTypeDescRepo = _taskTypeDescConfigRepositoryFactory.CreateRepository();
                using var taskDefinitionRepo = _taskDefinitionRepositoryFactory.CreateRepository();
                using var nodeInfoRepo = _nodeInfoRepositoryFactory.CreateRepository();
                foreach (var fireTaskParametersGroup in arrayPoolCollection.Where(static x => x != default)
                             .GroupBy(TaskParametersGroupFunc))
                {
                    var (taskDefinitionId, fireTaskInstanceId) = fireTaskParametersGroup.Key;

                    var taskDefinitionModel = await taskDefinitionRepo.GetByIdAsync(
                        taskDefinitionId,
                        cancellationToken);

                    if (taskDefinitionModel == null || string.IsNullOrEmpty(taskDefinitionModel.JobTypeDescId)) continue;
                    if (taskDefinitionModel.NodeList.Count == 0) continue;


                    taskDefinitionModel.TaskTypeDesc = await taskTypeDescRepo.GetByIdAsync(
                        taskDefinitionModel.JobTypeDescId,
                        cancellationToken);

                    await taskActivationRecordRepo.AddAsync(new TaskActivationRecordModel
                    {
                        Id = fireTaskInstanceId,
                        Name = taskDefinitionModel.Name,
                        TaskDefinitionJson = JsonSerializer.Serialize(taskDefinitionModel.Value)
                    }, cancellationToken);

                    foreach (var fireTaskParameters in fireTaskParametersGroup)
                    {
                        if (fireTaskParameters.NodeList != null && fireTaskParameters.NodeList.Count > 0)
                            taskDefinitionModel.NodeList = fireTaskParameters.NodeList;
                        await FireTaskAsync(
                            taskDefinitionRepo,
                            taskExecutionInstanceRepo,
                            taskActivationRecordRepo,
                            taskTypeDescRepo,
                            nodeInfoRepo,
                            fireTaskParameters,
                            taskDefinitionModel,
                            cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
            finally
            {

            }
    }
}