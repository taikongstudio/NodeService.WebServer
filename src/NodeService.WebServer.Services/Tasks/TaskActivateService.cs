using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.NodeSessions;
using OneOf;
using System.Threading.Channels;

namespace NodeService.WebServer.Services.Tasks;

public readonly record struct TaskFlowInfo
{
    public string TaskFlowTemplateId { get; init; }
    public string TaskFlowInstanceId { get; init; }
    public string? ParentTaskFlowInstanceId { get; init; }
}

public record struct FireTaskParameters
{
    public string TaskDefinitionId { get; init; }
    public string FireInstanceId { get; init; }
    public TriggerSource TriggerSource { get; init; }
    public DateTimeOffset? NextFireTimeUtc { get; init; }
    public DateTimeOffset? PreviousFireTimeUtc { get; init; }
    public DateTimeOffset? ScheduledFireTimeUtc { get; init; }
    public DateTimeOffset FireTimeUtc { get; init; }

    public string? ParentTaskId { get; init; }

    public List<StringEntry> NodeList { get; init; }

    public List<StringEntry> EnvironmentVariables { get; init; }

    public TaskFlowTaskKey TaskFlowTaskKey { get; init; }
}

public readonly struct FireTaskFlowParameters
{
    public string TaskFlowTemplateId { get; init; }
    public string TaskFlowInstanceId { get; init; }
    public string? TaskFlowParentInstanceId { get; init; }
    public TriggerSource TriggerSource { get; init; }
    public DateTimeOffset? NextFireTimeUtc { get; init; }
    public DateTimeOffset? PreviousFireTimeUtc { get; init; }
    public DateTimeOffset? ScheduledFireTimeUtc { get; init; }
    public DateTimeOffset FireTimeUtc { get; init; }
}

public readonly struct TaskActivateServiceParameters
{
    public TaskActivateServiceParameters(FireTaskParameters fireTaskParameters)
    {
        this.Parameters = fireTaskParameters;
    }

    public TaskActivateServiceParameters(FireTaskFlowParameters fireTaskFlowParameters)
    {
        this.Parameters = fireTaskFlowParameters;
    }

    public OneOf<FireTaskParameters, FireTaskFlowParameters> Parameters { get; init; }

}

public class TaskActivateService : BackgroundService
{
    readonly ILogger<TaskActivateService> _logger;
    readonly INodeSessionService _nodeSessionService;
    readonly ITaskPenddingContextManager _taskPenddingContextManager;
    readonly TaskFlowExecutor _taskFlowExecutor;
    readonly ExceptionCounter _exceptionCounter;
    readonly PriorityQueue<TaskPenddingContext, TaskExecutionPriority> _priorityQueue;
    readonly BatchQueue<TaskCancellationParameters> _taskCancellationQueue;
    readonly BatchQueue<TaskExecutionReportMessage> _taskExecutionReportBatchQueue;
    readonly BatchQueue<TaskActivateServiceParameters> _serviceParametersBatchQueue;
    readonly ApplicationRepositoryFactory<TaskDefinitionModel> _taskDefinitionRepositoryFactory;
    readonly ApplicationRepositoryFactory<TaskExecutionInstanceModel> _taskExecutionInstanceRepositoryFactory;
    readonly ApplicationRepositoryFactory<TaskActivationRecordModel> _taskActivationRecordRepositoryFactory;
    readonly ApplicationRepositoryFactory<TaskTypeDescConfigModel> _taskTypeDescConfigRepositoryFactory;
    readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepositoryFactory;
    readonly ApplicationRepositoryFactory<TaskFlowExecutionInstanceModel> _taskFlowExecutionInstanceRepoFactory;
    readonly ApplicationRepositoryFactory<TaskFlowTemplateModel> _taskFlowTemplateRepoFactory;

    public TaskActivateService(
        ILogger<TaskActivateService> logger,
        INodeSessionService nodeSessionService,
        ExceptionCounter exceptionCounter,
        ApplicationRepositoryFactory<TaskDefinitionModel> taskDefinitionRepositoryFactory,
        ApplicationRepositoryFactory<TaskExecutionInstanceModel> taskExecutionInstanceRepositoryFactory,
        ApplicationRepositoryFactory<TaskActivationRecordModel> taskActivationRepositoryFactory,
        ApplicationRepositoryFactory<TaskTypeDescConfigModel> taskTypeDescConfigRepositoryFactory,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepositoryFactory,
        ApplicationRepositoryFactory<TaskFlowTemplateModel> taskFlowTemplateRepoFactory,
        ApplicationRepositoryFactory<TaskFlowExecutionInstanceModel> taskFlowExecutionInstanceRepoFactory,
        BatchQueue<TaskExecutionReportMessage> taskExecutionReportBatchQueue,
        BatchQueue<TaskActivateServiceParameters> serviceParametersBatchQueue,
        BatchQueue<TaskCancellationParameters> taskCancellationQueue,
        ITaskPenddingContextManager taskPenddingContextManager,
        TaskFlowExecutor taskFlowExecutor)
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
        _taskFlowExecutionInstanceRepoFactory = taskFlowExecutionInstanceRepoFactory;
        _taskFlowTemplateRepoFactory = taskFlowTemplateRepoFactory;

        _taskExecutionReportBatchQueue = taskExecutionReportBatchQueue;
        _exceptionCounter = exceptionCounter;
        _serviceParametersBatchQueue = serviceParametersBatchQueue;
        _taskCancellationQueue = taskCancellationQueue;
        _taskPenddingContextManager = taskPenddingContextManager;

        _taskFlowExecutor = taskFlowExecutor;

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

    private async ValueTask ActivateRemoteNodeTasksAsync(
        IRepository<TaskExecutionInstanceModel> taskExecutionInstanceRepo,
        IRepository<NodeInfoModel> nodeInfoRepo,
        IRepository<TaskActivationRecordModel> taskActivationRecordRepo,
        FireTaskParameters parameters,
        TaskDefinitionModel taskDefinitionModel,
        TaskFlowTaskKey taskFlowTaskKey = default,
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

                    var nodeSessionIdList = _nodeSessionService.EnumNodeSessions(nodeId).ToArray();

                    if (nodeSessionIdList.Length == 0)
                    {
                        var nodeSessionId = new NodeSessionId(nodeId.Value);
                        nodeSessionIdList = [nodeSessionId];
                    }
                    foreach (var nodeSessionId in nodeSessionIdList)
                    {
                        var taskExecutionInstance = BuildTaskExecutionInstance(
                            taskDefinitionModel,
                            nodeSessionId,
                            parameters,
                            taskFlowTaskKey,
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


            _logger.LogInformation($"Task initialized {parameters.FireInstanceId}");
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
        TaskFlowTaskKey taskFlowTaskKey=default,
        CancellationToken cancellationToken = default)
    {
        var nodeName = _nodeSessionService.GetNodeName(nodeSessionId);
        var taskExecutionInstance = new TaskExecutionInstanceModel
        {
            Id = Guid.NewGuid().ToString(),
            Name = $"{nodeName} {taskDefinition.Name}",
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
                taskExecutionInstance.Message = $"{nodeName}: waiting for any task";
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

        taskExecutionInstance.TaskFlowTemplateId = taskFlowTaskKey.TaskFlowTemplateId;
        taskExecutionInstance.TaskFlowInstanceId = taskFlowTaskKey.TaskFlowInstanceId;
        taskExecutionInstance.TaskFlowStageId = taskFlowTaskKey.TaskFlowStageId;
        taskExecutionInstance.TaskFlowGroupId = taskFlowTaskKey.TaskFlowGroupId;
        taskExecutionInstance.TaskFlowTaskId = taskFlowTaskKey.TaskFlowTaskId;
        return taskExecutionInstance;
    }


    protected override async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(
            SchedulePenddingContextAsync(cancellationToken),
            ProcessTaskActivateServiceParametersAsync(cancellationToken));
    }

    private async Task ProcessTaskActivateServiceParametersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await foreach (var array in _serviceParametersBatchQueue.ReceiveAllAsync(cancellationToken))
            {
                try
                {
                    using var taskExecutionInstanceRepo = _taskExecutionInstanceRepositoryFactory.CreateRepository();
                    using var taskActivationRecordRepo = _taskActivationRecordRepositoryFactory.CreateRepository();
                    using var taskTypeDescRepo = _taskTypeDescConfigRepositoryFactory.CreateRepository();
                    using var taskDefinitionRepo = _taskDefinitionRepositoryFactory.CreateRepository();
                    using var nodeInfoRepo = _nodeInfoRepositoryFactory.CreateRepository();

                    foreach (var serviceParametersGroup in array.GroupBy(static x=>x.Parameters.Index))
                    {
                        switch (serviceParametersGroup.Key)
                        {
                            case 0:
                                await ProcessFireTaskParametersAsync(
                                    taskExecutionInstanceRepo,
                                    taskActivationRecordRepo,
                                    taskTypeDescRepo,
                                    taskDefinitionRepo,
                                    nodeInfoRepo,
                                    serviceParametersGroup.Select(x => x.Parameters.AsT0),
                                    cancellationToken);

                                break;
                            case 1:
                                await ProcessTaskFlowFireParametersAsync(
                                    serviceParametersGroup.Select(x => x.Parameters.AsT1),
                                    cancellationToken);
                                break;
                            default:
                                break;
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

    async ValueTask ProcessTaskFlowFireParametersAsync(IEnumerable<FireTaskFlowParameters> fireTaskFlowParameterList, CancellationToken cancellationToken)
    {
        using var taskFlowTemplateRepo = _taskFlowTemplateRepoFactory.CreateRepository();
        using var taskFlowExeuctionInstanceRepo = _taskFlowExecutionInstanceRepoFactory.CreateRepository();
        foreach (var fireTaskFlowParametersGroup in fireTaskFlowParameterList.GroupBy(static x => x.TaskFlowTemplateId))
        {
            var taskFlowTemplateId = fireTaskFlowParametersGroup.Key;
            var taskFlowTemplate = await taskFlowTemplateRepo.GetByIdAsync(taskFlowTemplateId, cancellationToken);
            if (taskFlowTemplate == null)
            {
                continue;
            }
            List<TaskFlowExecutionInstanceModel> taskFlowExecutionInstances = [];
            foreach (var fireTaskFlowParameters in fireTaskFlowParametersGroup)
            {
                var taskFlowExecutionInstance = new TaskFlowExecutionInstanceModel()
                {
                    Id = fireTaskFlowParameters.TaskFlowInstanceId,
                    Name = taskFlowTemplate.Name,
                    CreationDateTime = DateTime.UtcNow,
                    ModifiedDateTime = DateTime.UtcNow
                };
                taskFlowExecutionInstance.Value.Id = fireTaskFlowParameters.TaskFlowInstanceId;
                taskFlowExecutionInstance.Value.Name = taskFlowTemplate.Name;
                taskFlowExecutionInstance.Value.CreationDateTime = DateTime.UtcNow;
                taskFlowExecutionInstance.Value.ModifiedDateTime = DateTime.UtcNow;
                taskFlowExecutionInstance.Value.TaskFlowTemplateId = taskFlowTemplateId;
                foreach (var taskFlowStageTemplate in taskFlowTemplate.Value.TaskStages)
                {
                    var taskFlowStageExecutionInstance = new TaskFlowStageExecutionInstance()
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = taskFlowStageTemplate.Name,
                        CreationDateTime = DateTime.UtcNow,
                        ModifiedDateTime = DateTime.UtcNow,
                        TaskFlowStageTemplateId = taskFlowStageTemplate.Id
                    };
                    taskFlowExecutionInstance.Value.TaskStages.Add(taskFlowStageExecutionInstance);
                    foreach (var taskFlowGroupTemplate in taskFlowStageTemplate.TaskGroups)
                    {
                        var taskFlowGroupExeuctionInstance = new TaskFlowGroupExecutionInstance()
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = taskFlowGroupTemplate.Name,
                            CreationDateTime = DateTime.UtcNow,
                            ModifiedDateTime = DateTime.UtcNow,
                            TaskFlowGroupTemplateId = taskFlowGroupTemplate.Id
                        };
                        taskFlowStageExecutionInstance.TaskGroups.Add(taskFlowGroupExeuctionInstance);
                        foreach (var taskFlowTaskTemplate in taskFlowGroupTemplate.Tasks)
                        {
                            var taskFlowTaskExecutionInstance = new TaskFlowTaskExecutionInstance()
                            {
                                Id = Guid.NewGuid().ToString(),
                                Name = taskFlowTaskTemplate.Name,
                                CreationDateTime = DateTime.UtcNow,
                                ModifiedDateTime = DateTime.UtcNow,
                                Status = TaskExecutionStatus.Unknown,
                                TaskFlowTaskTemplateId = taskFlowTaskTemplate.Id,
                                TaskDefinitionId = taskFlowTaskTemplate.TaskDefinitionId,
                            };
                            taskFlowGroupExeuctionInstance.Tasks.Add(taskFlowTaskExecutionInstance);
                        }
                    }
                }
                taskFlowExecutionInstances.Add(taskFlowExecutionInstance);
            }
            foreach (var taskFlowExecutionInstance in taskFlowExecutionInstances)
            {
                await _taskFlowExecutor.ExecuteAsync(taskFlowExecutionInstance, cancellationToken);
            }
            foreach (var array in taskFlowExecutionInstances.Chunk(10))
            {
                await taskFlowExeuctionInstanceRepo.AddRangeAsync(array, cancellationToken);
            }
        }

    }

    async Task ProcessFireTaskParametersAsync(
        IRepository<TaskExecutionInstanceModel> taskExecutionInstanceRepo, 
        IRepository<TaskActivationRecordModel> taskActivationRecordRepo,
        IRepository<TaskTypeDescConfigModel> taskTypeDescRepo, 
        IRepository<TaskDefinitionModel> taskDefinitionRepo,
        IRepository<NodeInfoModel> nodeInfoRepo, 
        IEnumerable<FireTaskParameters> fireTaskParameterList, 
        CancellationToken cancellationToken)
    {

        foreach (var fireTaskParameterGroup in fireTaskParameterList.GroupBy(static x => x.TaskDefinitionId))
        {
            var taskDefinitionId = fireTaskParameterGroup.Key;
            var taskDefinition = await taskDefinitionRepo.GetByIdAsync(
                taskDefinitionId,
                cancellationToken);
            if (taskDefinition == null)
            {
                continue;
            }
            foreach (var fireTaskParameters in fireTaskParameterGroup)
            {

                if (taskDefinition == null || string.IsNullOrEmpty(taskDefinition.TaskTypeDescId))
                    continue;

                taskDefinition.TaskTypeDesc = await taskTypeDescRepo.GetByIdAsync(
                                                                                taskDefinition.TaskTypeDescId,
                                                                                cancellationToken);

                if (fireTaskParameters.NodeList != null && fireTaskParameters.NodeList.Count > 0)
                {
                    taskDefinition.NodeList = fireTaskParameters.NodeList;
                }
                await ActivateRemoteNodeTasksAsync(
                     taskExecutionInstanceRepo,
                     nodeInfoRepo,
                     taskActivationRecordRepo,
                     fireTaskParameters,
                     taskDefinition,
                     fireTaskParameters.TaskFlowTaskKey,
                     cancellationToken);
            }

        }


    }
}