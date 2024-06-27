using System.ComponentModel.DataAnnotations;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Models;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.NodeSessions;

namespace NodeService.WebServer.Services.Tasks;

public class TaskActivationService : BackgroundService
{
    private readonly ExceptionCounter _exceptionCounter;
    private readonly BatchQueue<FireTaskParameters> _fireTaskBatchQueue;
    private readonly ILogger<TaskActivationService> _logger;
    private readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepositoryFactory;
    private readonly INodeSessionService _nodeSessionService;
    private readonly PriorityQueue<TaskPenddingContext, TaskExecutionPriority> _priorityQueue;
    private readonly BatchQueue<TaskCancellationParameters> _taskCancellationQueue;
    private readonly ITaskPenddingContextManager _taskPenddingContextManager;
    private readonly ApplicationRepositoryFactory<TaskDefinitionModel> _taskDefinitionRepositoryFactory;
    private readonly ApplicationRepositoryFactory<TaskExecutionInstanceModel> _taskExecutionInstanceRepositoryFactory;
    private readonly BatchQueue<TaskExecutionReportMessage> _taskExecutionReportBatchQueue;
    private readonly ApplicationRepositoryFactory<TaskActivationRecordModel> _taskActivationRecordRepositoryFactory;
    private readonly ApplicationRepositoryFactory<TaskTypeDescConfigModel> _taskTypeDescConfigRepositoryFactory;

    public TaskActivationService(
        ILogger<TaskActivationService> logger,
        INodeSessionService nodeSessionService,
        ExceptionCounter exceptionCounter,
        ApplicationRepositoryFactory<TaskDefinitionModel> taskDefinitionRepositoryFactory,
        ApplicationRepositoryFactory<TaskExecutionInstanceModel> taskExecutionInstanceRepositoryFactory,
        ApplicationRepositoryFactory<TaskActivationRecordModel> taskActivationRepositoryFactory,
        ApplicationRepositoryFactory<TaskTypeDescConfigModel> taskTypeDescConfigRepositoryFactory,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepositoryFactory,
        BatchQueue<TaskExecutionReportMessage> taskExecutionReportBatchQueue,
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
        _taskActivationRecordRepositoryFactory = taskActivationRepositoryFactory;
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

    private async Task SchedulePenddingContextAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await PenddingContextChannel.Reader.WaitToReadAsync(cancellationToken);
                while (PenddingContextChannel.Reader.TryRead(out var penddingContext))
                    _priorityQueue.Enqueue(penddingContext, penddingContext.TaskDefinition.Priority);
                while (_priorityQueue.Count > 0)
                {
                    while (PenddingActionBlock.InputCount < 128
                           &&
                           _priorityQueue.TryDequeue(out var penddingContext, out var _))
                        PenddingActionBlock.Post(penddingContext);

                    await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }

    }

    private async Task ProcessPenddingContextAsync(TaskPenddingContext context)
    {
        var readyToRun = false;
        try
        {
            using var repo = _taskExecutionInstanceRepositoryFactory.CreateRepository();
            _logger.LogInformation($"{context.Id}:Start init");
            context.EnsureInit();
            switch (context.TaskDefinition.ExecutionStrategy)
            {
                case TaskExecutionStrategy.Concurrent:
                    readyToRun = true;
                    break;
                case TaskExecutionStrategy.Queue:
                    readyToRun = await context.WaitForRunningTasksAsync(repo);
                    break;
                case TaskExecutionStrategy.Stop:
                    readyToRun = await context.StopRunningTasksAsync(repo, _taskCancellationQueue);
                    break;
                case TaskExecutionStrategy.Skip:
                    return;
            }

            if (readyToRun)
            {
                while (!context.CancellationToken.IsCancellationRequested)
                {
                    if (context.NodeSessionService.GetNodeStatus(context.NodeSessionId) == NodeStatus.Online) break;
                    await Task.Delay(TimeSpan.FromSeconds(5), context.CancellationToken);
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
            _exceptionCounter.AddOrUpdate(ex, context.Id);
            _logger.LogError($"{context.Id}:{ex}");
        }
        catch (OperationCanceledException ex)
        {
            if (ex.CancellationToken == context.CancellationToken)
            {
            }

            _exceptionCounter.AddOrUpdate(ex, context.Id);
            _logger.LogError($"{context.Id}:{ex}");
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex, context.Id);
            _logger.LogError(ex.ToString());
        }
        finally
        {
            _taskPenddingContextManager.RemoveContext(context.Id, out _);
        }
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

    private async ValueTask ActivateTasksAsync(
        IRepository<TaskExecutionInstanceModel> taskExecutionInstanceRepo,
        IRepository<NodeInfoModel> nodeInfoRepo,
        IRepository<TaskActivationRecordModel> taskActivationRecordRepo,
        FireTaskParameters parameters,
        TaskDefinitionModel taskDefinitionModel,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var nodeInfoList = await nodeInfoRepo.ListAsync(
                new NodeInfoSpecification(
                    AreaTags.Any,
                    NodeStatus.All,
                    NodeDeviceType.Computer,
                    DataFilterCollection<string>.Includes(taskDefinitionModel.NodeList.Select(x => x.Value))),
                cancellationToken);

            var taskExecutionInstanceList = new List<KeyValuePair<NodeSessionId, TaskExecutionInstanceModel>>();

            foreach (var nodeEntry in taskDefinitionModel.NodeList)
            {
                try
                {
                    if (string.IsNullOrEmpty(nodeEntry.Value)) continue;
                    var nodeId = NodeId.Null;
                    foreach (var nodeInfo in nodeInfoList)
                        if (nodeInfo.Id == nodeEntry.Value)
                        {
                            nodeId = new NodeId(nodeEntry.Value);
                            break;
                        }

                    if (nodeId.IsNullOrEmpty) continue;

                    var sessions = _nodeSessionService.EnumNodeSessions(nodeId).ToArray();

                    foreach (var nodeSessionId in _nodeSessionService.EnumNodeSessions(nodeId))
                    {
                        var taskExecutionInstance = BuildTaskExecutionInstance(
                            taskDefinitionModel,
                            nodeSessionId,
                            parameters,
                            cancellationToken);
                        taskExecutionInstanceList.Add(KeyValuePair.Create(nodeSessionId, taskExecutionInstance));
                    }
                }
                catch (Exception ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }
            }
            await taskExecutionInstanceRepo.AddRangeAsync(taskExecutionInstanceList.Select(static x => x.Value), cancellationToken);

            var taskExecutionInstanceInfoList = taskExecutionInstanceList.Select(x => new TaskExecutionInstanceInfo()
            {
                NodeInfoId = x.Value.NodeInfoId,
                TaskExecutionInstanceId = x.Value.Id,
                Status = TaskExecutionStatus.Unknown
            }).ToList();

            await taskActivationRecordRepo.AddAsync(new TaskActivationRecordModel
            {
                Id = parameters.FireInstanceId,
                TaskDefinitionId = taskDefinitionModel.Id,
                Name = taskDefinitionModel.Name,
                TaskDefinitionJson = JsonSerializer.Serialize(taskDefinitionModel.Value),
                TaskExecutionInstanceInfoList = taskExecutionInstanceInfoList,
                TotalCount = taskExecutionInstanceInfoList.Count,
                Status = TaskExecutionStatus.Unknown
            }, cancellationToken);

            foreach (var kv in taskExecutionInstanceList)
            {
                var taskExecutionInstance = kv.Value;
                var context = new TaskPenddingContext(taskExecutionInstance.Id)
                {
                    NodeSessionService = _nodeSessionService,
                    NodeSessionId = new NodeSessionId(taskExecutionInstance.NodeInfoId),
                    TriggerEvent = taskExecutionInstance.ToTriggerEvent(taskDefinitionModel, parameters.EnvironmentVariables),
                    FireParameters = parameters,
                    TaskDefinition = taskDefinitionModel
                };

                if (_taskPenddingContextManager.AddContext(context))
                    await PenddingContextChannel.Writer.WriteAsync(context, cancellationToken);

            }


            _logger.LogInformation($"Job initialized {parameters.FireInstanceId}");
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    public TaskExecutionInstanceModel BuildTaskExecutionInstance(
        TaskDefinitionModel taskDefinition,
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
            TaskDefinitionId = taskDefinition.Id,
            ParentId = parameters.ParentTaskId,
            FireInstanceId = parameters.FireInstanceId
        };
        taskExecutionInstance.Status = TaskExecutionStatus.Pendding;
        switch (taskDefinition.ExecutionStrategy)
        {
            case TaskExecutionStrategy.Concurrent:
                taskExecutionInstance.Message = $"{nodeName}: triggered";
                break;
            case TaskExecutionStrategy.Queue:
                taskExecutionInstance.Message = $"{nodeName}: waiting for any job";
                break;
            case TaskExecutionStrategy.Skip:
                taskExecutionInstance.Message = $"{nodeName}: start job";
                break;
            case TaskExecutionStrategy.Stop:
                taskExecutionInstance.Message = $"{nodeName}: waiting for kill all job";
                break;
        }

        if (parameters.NextFireTimeUtc != null)
            taskExecutionInstance.NextFireTimeUtc = parameters.NextFireTimeUtc.Value.UtcDateTime;
        if (parameters.PreviousFireTimeUtc != null)
            taskExecutionInstance.NextFireTimeUtc = parameters.PreviousFireTimeUtc.Value.UtcDateTime;
        if (parameters.ScheduledFireTimeUtc != null)
            taskExecutionInstance.ScheduledFireTimeUtc = parameters.ScheduledFireTimeUtc.Value.UtcDateTime;

        return taskExecutionInstance;
    }

    private (string TaskDefinitionId, string TaskFireInstanceId) TaskParametersGroupFunc(
        FireTaskParameters fireTaskParameters)
    {
        return (fireTaskParameters.TaskDefinitionId, fireTaskParameters.FireInstanceId);
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(
            SchedulePenddingContextAsync(cancellationToken),
            ProcessTaskFireParametersAsync(cancellationToken));
    }

    private async Task ProcessTaskFireParametersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await foreach (var array in _fireTaskBatchQueue.ReceiveAllAsync(cancellationToken))
            {
                try
                {
                    using var taskExecutionInstanceRepo = _taskExecutionInstanceRepositoryFactory.CreateRepository();
                    using var taskActivationRecordRepo = _taskActivationRecordRepositoryFactory.CreateRepository();
                    using var taskTypeDescRepo = _taskTypeDescConfigRepositoryFactory.CreateRepository();
                    using var taskDefinitionRepo = _taskDefinitionRepositoryFactory.CreateRepository();
                    using var nodeInfoRepo = _nodeInfoRepositoryFactory.CreateRepository();
                    foreach (var fireTaskParametersGroup in array.Where(static x => x != default)
                                 .GroupBy(TaskParametersGroupFunc))
                    {
                        var (taskDefinitionId, fireTaskInstanceId) = fireTaskParametersGroup.Key;

                        var taskDefinitionModel = await taskDefinitionRepo.GetByIdAsync(
                            taskDefinitionId,
                            cancellationToken);

                        if (taskDefinitionModel == null || string.IsNullOrEmpty(taskDefinitionModel.JobTypeDescId))
                            continue;

                        taskDefinitionModel.TaskTypeDesc = await taskTypeDescRepo.GetByIdAsync(
                            taskDefinitionModel.JobTypeDescId,
                            cancellationToken);

                        foreach (var fireTaskParameters in fireTaskParametersGroup)
                        {
                            if (fireTaskParameters.NodeList != null && fireTaskParameters.NodeList.Count > 0)
                                taskDefinitionModel.NodeList = fireTaskParameters.NodeList;
                            await ActivateTasksAsync(
                                 taskExecutionInstanceRepo,
                                 nodeInfoRepo,
                                 taskActivationRecordRepo,
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
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }

    }
}