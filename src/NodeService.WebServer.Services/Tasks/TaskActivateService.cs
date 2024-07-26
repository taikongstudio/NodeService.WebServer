using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Models;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
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

public readonly record struct RetryTaskParameters
{
    public RetryTaskParameters(string taskExecutionInstanceId)
    {
        TaskExecutionInstanceId = taskExecutionInstanceId;
    }

    public string TaskExecutionInstanceId { get; init; }
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

    public string? ParentTaskExecutionInstanceId { get; init; }

    public List<StringEntry> NodeList { get; init; }

    public List<StringEntry> EnvironmentVariables { get; init; }

    public TaskFlowTaskKey TaskFlowTaskKey { get; init; }

    public bool RetryTasks { get; set; }

    public static FireTaskParameters BuildRetryTaskParameters(
        string taskActiveRecordId,
        string taskDefinitionId,
        string fireInstanceId,
        List<StringEntry> nodeList,
        TaskFlowTaskKey taskFlowTaskKey = default)
    {
        return new FireTaskParameters
        {
            FireTimeUtc = DateTime.UtcNow,
            TriggerSource = TriggerSource.Manual,
            FireInstanceId = fireInstanceId,
            TaskDefinitionId = taskDefinitionId,
            ScheduledFireTimeUtc = DateTime.UtcNow,
            NodeList = nodeList,
            EnvironmentVariables = [],
            TaskFlowTaskKey = taskFlowTaskKey,
            RetryTasks = true,
        };
    }
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

public readonly record struct SwitchStageParameters
{
    public SwitchStageParameters(string taskFlowExecutionInstanceId, int stageIndex)
    {
        TaskFlowExecutionInstanceId = taskFlowExecutionInstanceId;
        StageIndex = stageIndex;
    }

    public string TaskFlowExecutionInstanceId { get; init; }

    public int StageIndex { get; init; }
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

    public TaskActivateServiceParameters(RetryTaskParameters retryTaskParameters)
    {
        Parameters = retryTaskParameters;
    }
    public TaskActivateServiceParameters(SwitchStageParameters  switchTaskFlowStageParameters)
    {
        Parameters = switchTaskFlowStageParameters;
    }

    public OneOf<FireTaskParameters, FireTaskFlowParameters, RetryTaskParameters, SwitchStageParameters> Parameters { get; init; }

}

public class TaskActivateService : BackgroundService
{
    readonly ILogger<TaskActivateService> _logger;
    readonly INodeSessionService _nodeSessionService;
    readonly ITaskPenddingContextManager _taskPenddingContextManager;
    readonly TaskFlowExecutor _taskFlowExecutor;
    readonly NodeInfoQueryService _nodeInfoQueryService;
    readonly ConfigurationQueryService _configurationQueryService;
    readonly ExceptionCounter _exceptionCounter;
    readonly PriorityQueue<TaskPenddingContext, TaskExecutionPriority> _priorityQueue;
    readonly BatchQueue<TaskCancellationParameters> _taskCancellationQueue;
    readonly BatchQueue<TaskExecutionReportMessage> _taskExecutionReportBatchQueue;
    readonly BatchQueue<TaskActivateServiceParameters> _serviceParametersBatchQueue;
    readonly ApplicationRepositoryFactory<TaskDefinitionModel> _taskDefinitionRepositoryFactory;
    readonly ApplicationRepositoryFactory<TaskExecutionInstanceModel> _taskExecutionInstanceRepositoryFactory;
    readonly ApplicationRepositoryFactory<TaskActivationRecordModel> _taskActivationRecordRepoFactory;
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
        TaskFlowExecutor taskFlowExecutor,
        NodeInfoQueryService nodeInfoQueryService,
        ConfigurationQueryService configurationQueryService)
    {
        _logger = logger;
        PenddingContextChannel = Channel.CreateUnbounded<TaskPenddingContext>();
        _priorityQueue = new PriorityQueue<TaskPenddingContext, TaskExecutionPriority>();
        _nodeSessionService = nodeSessionService;
        _taskDefinitionRepositoryFactory = taskDefinitionRepositoryFactory;
        _taskExecutionInstanceRepositoryFactory = taskExecutionInstanceRepositoryFactory;
        _taskActivationRecordRepoFactory = taskActivationRepositoryFactory;
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
        _nodeInfoQueryService = nodeInfoQueryService;
        _configurationQueryService = configurationQueryService;
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
                {
                    _priorityQueue.Enqueue(penddingContext, penddingContext.TaskDefinition.Priority);
                }
                while (_priorityQueue.Count > 0)
                {
                    while (PenddingActionBlock.InputCount < 128
                           &&
                           _priorityQueue.TryDequeue(out var penddingContext, out var _))
                    {
                        var nodeStatus = _nodeSessionService.GetNodeStatus(penddingContext.NodeSessionId);
                        if (nodeStatus == NodeStatus.Online || penddingContext.CancellationToken.IsCancellationRequested)
                        {
                            PenddingActionBlock.Post(penddingContext);
                        }
                        else
                        {
                            bool isPenddingTimeOut = false;
                            var timeSpan = DateTime.UtcNow - penddingContext.FireParameters.FireTimeUtc;
                            if (penddingContext.TaskDefinition.PenddingLimitTimeSeconds == 0)
                            {
                                isPenddingTimeOut = true;
                            }
                            else if (penddingContext.TaskDefinition.PenddingLimitTimeSeconds > 0
                                &&
                                 timeSpan > TimeSpan.FromSeconds(penddingContext.TaskDefinition.PenddingLimitTimeSeconds))
                            {
                                isPenddingTimeOut = true;
                            }

                            if (isPenddingTimeOut)
                            {
                                await SendTaskExecutionReportAsync(
                                        penddingContext.Id,
                                        TaskExecutionStatus.PenddingTimeout,
                                        $"Time out after waiting {penddingContext.TaskDefinition.PenddingLimitTimeSeconds} seconds");
                                _logger.LogInformation($"{penddingContext.Id}:SendAsync PenddingTimeout");
                                continue;
                            }

                            await PenddingContextChannel.Writer.WriteAsync(penddingContext, cancellationToken);
                        }
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }

    }

    async ValueTask<bool> HasAnyActiveTaskAsync(TaskPenddingContext context, CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var taskExecutionInstanceRepo = await _taskExecutionInstanceRepositoryFactory.CreateRepositoryAsync(cancellationToken);
            if (await context.WaitForRunningTasksAsync(taskExecutionInstanceRepo))
            {
                return true;
            }
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
        return false;
    }

    async ValueTask<bool> StopAnyActiveTaskAsync(
        TaskPenddingContext context,
        BatchQueue<TaskCancellationParameters> batchQueue,
        CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var taskExecutionInstanceRepo = await _taskExecutionInstanceRepositoryFactory.CreateRepositoryAsync(cancellationToken);
            if (await context.StopRunningTasksAsync(taskExecutionInstanceRepo, batchQueue))
            {
                return true;
            }
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
        return false;
    }

    private async Task ProcessPenddingContextAsync(TaskPenddingContext context)
    {
        var readyToRun = false;
        try
        {
            if (!context.CancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation($"{context.Id}:Start init");
                switch (context.TaskDefinition.ExecutionStrategy)
                {
                    case TaskExecutionStrategy.Concurrent:
                        readyToRun = true;
                        break;
                    case TaskExecutionStrategy.Queue:
                        readyToRun = await HasAnyActiveTaskAsync(context, context.CancellationToken);
                        break;
                    case TaskExecutionStrategy.Stop:
                        readyToRun = await StopAnyActiveTaskAsync(context, _taskCancellationQueue);
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
            var exString = ex.ToString();
            _exceptionCounter.AddOrUpdate(ex, context.Id);
            _logger.LogError(exString);
            await SendTaskExecutionReportAsync(
                context.Id,
                TaskExecutionStatus.Failed,
                exString);
        }
        finally
        {
            _taskPenddingContextManager.RemoveContext(context.Id, out _);
        }
        if (context.CancellationToken.IsCancellationRequested)
        {
            await SendTaskExecutionReportAsync(
                context.Id,
                TaskExecutionStatus.PenddingTimeout,
                $"Time out after waiting {context.TaskDefinition.PenddingLimitTimeSeconds} seconds");
            _logger.LogInformation($"{context.Id}:SendAsync PenddingTimeout");
        }
    }

    public async Task SendTaskExecutionReportAsync(string taskExecutionInstanceId, TaskExecutionStatus status, string message)
    {
        await _taskExecutionReportBatchQueue.SendAsync(new TaskExecutionReportMessage
        {
            Message = new TaskExecutionReport
            {
                Id = taskExecutionInstanceId,
                Status = status,
                Message = message
            }
        });
    }

    async ValueTask ActivateRemoteNodeTasksAsync(
        FireTaskParameters fireTaskParameters,
        TaskDefinitionModel taskDefinition,
        TaskFlowTaskKey taskFlowTaskKey = default,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var taskExecutionInstanceList = new List<KeyValuePair<NodeSessionId, TaskExecutionInstanceModel>>();

            if (fireTaskParameters.RetryTasks)
            {
                await using var taskActivationRecordRepo = await _taskActivationRecordRepoFactory.CreateRepositoryAsync(cancellationToken);
                var taskActivationRecord = await taskActivationRecordRepo.GetByIdAsync(fireTaskParameters.FireInstanceId, cancellationToken);
                if (taskActivationRecord == null)
                {
                    return;
                }
                if (taskActivationRecord.Status == TaskExecutionStatus.Finished)
                {
                    return;
                }
                var nodeInfoList = await QueryNodeListAsync(taskDefinition, cancellationToken);

                foreach (var taskExecutionNodeInfo in taskActivationRecord.Value.TaskExecutionNodeList)
                {
                    if (taskExecutionNodeInfo.Status == TaskExecutionStatus.Finished)
                    {
                        continue;
                    }
                    var lastInstance = taskExecutionNodeInfo.Instances.LastOrDefault();
                    if (lastInstance == null)
                    {
                        continue;
                    }

                    bool canRetry = lastInstance.Status switch
                    {
                        TaskExecutionStatus.Failed or TaskExecutionStatus.PenddingTimeout or TaskExecutionStatus.Failed => true,
                        _ => false
                    };

                    if (!canRetry)
                    {
                        continue;
                    }
                    var nodeInfoFindResult = FindNodeInfo(nodeInfoList, taskExecutionNodeInfo.NodeInfoId);
                    if (nodeInfoFindResult == default)
                    {
                        continue;
                    }

                    var nodeEntries = taskDefinition.NodeList.Where(x => x.Value == taskExecutionNodeInfo.NodeInfoId);
                    if (!nodeEntries.Any())
                    {
                        continue;
                    }

                    BuildTaskExecutionInstanceList(
                        taskExecutionInstanceList,
                        fireTaskParameters,
                        taskDefinition,
                        taskFlowTaskKey,
                        nodeEntries,
                        [nodeInfoFindResult.NodeInfo],
                        cancellationToken);

                    foreach (var nodeEntry in nodeEntries)
                    {
                        AddTaskExecutionNodeInstances(taskExecutionInstanceList, nodeEntry, taskExecutionNodeInfo);
                    }

                }

                await AddTaskExecutionInstanceListAsync(
                        taskExecutionInstanceList.Select(static x => x.Value),
                        cancellationToken);

                await taskActivationRecordRepo.UpdateAsync(taskActivationRecord, cancellationToken);
                await ProcessTaskExecutionInstanceListAsync(
                                    fireTaskParameters,
                                    taskDefinition,
                                    taskExecutionInstanceList,
                                    cancellationToken);
            }
            else
            {
                if (fireTaskParameters.NodeList != null && fireTaskParameters.NodeList.Count > 0)
                {
                    taskDefinition.NodeList = fireTaskParameters.NodeList;
                }

                var nodeInfoList = await QueryNodeListAsync(taskDefinition, cancellationToken);

                BuildTaskExecutionInstanceList(
                    taskExecutionInstanceList,
                    fireTaskParameters,
                    taskDefinition,
                    taskFlowTaskKey,
                    taskDefinition.NodeList,
                    nodeInfoList,
                    cancellationToken);

                await AddTaskExecutionInstanceListAsync(
                    taskExecutionInstanceList.Select(static x => x.Value),
                    cancellationToken);

                List<TaskExecutionNodeInfo> taskExecutionNodeList = [];

                foreach (var nodeEntry in taskDefinition.NodeList)
                {
                    if (nodeEntry == null || nodeEntry.Value == null)
                    {
                        continue;
                    }
                    var taskExecutionNodeInfo = new TaskExecutionNodeInfo(fireTaskParameters.FireInstanceId, nodeEntry.Value);
                    AddTaskExecutionNodeInstances(taskExecutionInstanceList, nodeEntry, taskExecutionNodeInfo);
                    taskExecutionNodeList.Add(taskExecutionNodeInfo);
                }

                var taskActivationRecord = new TaskActivationRecordModel
                {
                    CreationDateTime = DateTime.UtcNow,
                    ModifiedDateTime = DateTime.UtcNow,
                    Id = fireTaskParameters.FireInstanceId,
                    TaskDefinitionId = taskDefinition.Id,
                    Name = taskDefinition.Name,
                    TaskDefinitionJson = JsonSerializer.Serialize(taskDefinition.Value),
                    TaskExecutionNodeList = taskExecutionNodeList,
                    TotalCount = taskExecutionNodeList.Count,
                    Status = TaskExecutionStatus.Unknown,
                    TaskFlowTemplateId = taskFlowTaskKey.TaskFlowTemplateId,
                    TaskFlowTaskId = taskFlowTaskKey.TaskFlowTaskId,
                    TaskFlowInstanceId = taskFlowTaskKey.TaskFlowInstanceId,
                    TaskFlowGroupId = taskFlowTaskKey.TaskFlowGroupId,
                    TaskFlowStageId = taskFlowTaskKey.TaskFlowStageId,
                    NodeList = [.. taskDefinition.NodeList]
                };
                await using var taskActivationRecordRepo = await _taskActivationRecordRepoFactory.CreateRepositoryAsync(cancellationToken);
                await taskActivationRecordRepo.AddAsync(taskActivationRecord, cancellationToken);
                await ProcessTaskExecutionInstanceListAsync(
                                    fireTaskParameters,
                                    taskDefinition,
                                    taskExecutionInstanceList,
                                    cancellationToken);
            }

            _logger.LogInformation($"Task initialized {fireTaskParameters.FireInstanceId}");
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    static void AddTaskExecutionNodeInstances(
        List<KeyValuePair<NodeSessionId, TaskExecutionInstanceModel>> taskExecutionInstanceList,
        StringEntry? nodeEntry,
        TaskExecutionNodeInfo taskExecutionNodeInfo)
    {
        foreach (var item in taskExecutionInstanceList)
        {
            if (item.Key.NodeId.Value == nodeEntry.Value)
            {
                taskExecutionNodeInfo.Instances.Add(new TaskExecutionInstanceInfo()
                {
                    TaskExecutionInstanceId = item.Value.Id,
                    Status = TaskExecutionStatus.Unknown,
                    Message = item.Value.Message,
                });
                break;
            }
        }
    }

    void BuildTaskExecutionInstanceList(
        List<KeyValuePair<NodeSessionId, TaskExecutionInstanceModel>> taskExecutionInstanceList,
        FireTaskParameters fireTaskParameters,
        TaskDefinitionModel taskDefinition,
        TaskFlowTaskKey taskFlowTaskKey,
        IEnumerable<StringEntry> nodeList,
        IEnumerable<NodeInfoModel> nodeInfoList,
        CancellationToken cancellationToken)
    {
        foreach (var nodeEntry in nodeList)
        {
            try
            {
                if (string.IsNullOrEmpty(nodeEntry.Value)) continue;
                var nodeFindResult = FindNodeInfo(nodeInfoList, nodeEntry.Value);

                if (nodeFindResult.NodeId.IsNullOrEmpty) continue;

                var nodeSessionIdList = _nodeSessionService.EnumNodeSessions(nodeFindResult.NodeId).ToArray();

                if (nodeSessionIdList.Length == 0)
                {
                    var nodeSessionId = new NodeSessionId(nodeFindResult.NodeId.Value);
                    nodeSessionIdList = [nodeSessionId];
                }
                foreach (var nodeSessionId in nodeSessionIdList)
                {
                    var taskExecutionInstance = BuildTaskExecutionInstance(
                        nodeFindResult.NodeInfo,
                        taskDefinition,
                        nodeSessionId,
                        fireTaskParameters,
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
    }

    async ValueTask ProcessTaskExecutionInstanceListAsync(
        FireTaskParameters fireTaskParameters,
        TaskDefinitionModel taskDefinition,
        List<KeyValuePair<NodeSessionId, TaskExecutionInstanceModel>> taskExecutionInstanceList,
        CancellationToken cancellationToken = default)
    {
        foreach (var kv in taskExecutionInstanceList)
        {
            var taskExecutionInstance = kv.Value;
            var context = new TaskPenddingContext(taskExecutionInstance.Id)
            {
                NodeSessionService = _nodeSessionService,
                NodeSessionId = new NodeSessionId(taskExecutionInstance.NodeInfoId),
                TriggerEvent = taskExecutionInstance.ToTriggerEvent(taskDefinition, fireTaskParameters.EnvironmentVariables),
                FireParameters = fireTaskParameters,
                TaskDefinition = taskDefinition
            };

            if (_taskPenddingContextManager.AddContext(context))
            {
                context.EnsureInit();
                await PenddingContextChannel.Writer.WriteAsync(context, cancellationToken);
            }
            await SendTaskExecutionReportAsync(taskExecutionInstance.Id, TaskExecutionStatus.Triggered, string.Empty);
        }
    }

    async ValueTask AddTaskExecutionInstanceListAsync(
        IEnumerable<TaskExecutionInstanceModel> taskExecutionInstanceList,
        CancellationToken cancellationToken = default)
    {
        await using var taskExecutionInstanceRepo = await _taskExecutionInstanceRepositoryFactory.CreateRepositoryAsync(cancellationToken);
        await taskExecutionInstanceRepo.AddRangeAsync(taskExecutionInstanceList, cancellationToken);
    }

     ValueTask<List<NodeInfoModel>> QueryNodeListAsync(TaskDefinitionModel taskDefinition, CancellationToken cancellationToken = default)
    {
        return _nodeInfoQueryService.QueryNodeInfoListAsync(taskDefinition.NodeList.Select(static x => x.Value), true, cancellationToken);
    }

    static (NodeId NodeId, NodeInfoModel NodeInfo) FindNodeInfo(IEnumerable<NodeInfoModel> nodeInfoList, string id)
    {
        foreach (var nodeInfo in nodeInfoList)
        {
            if (nodeInfo.Id == id)
            {
                return (new NodeId(nodeInfo.Id), nodeInfo);
            }
        }

        return default;
    }

    TaskExecutionInstanceModel BuildTaskExecutionInstance(
        NodeInfoModel nodeInfo,
        TaskDefinitionModel taskDefinition,
        NodeSessionId nodeSessionId,
        FireTaskParameters parameters,
        TaskFlowTaskKey taskFlowTaskKey = default,
        CancellationToken cancellationToken = default)
    {
        var nodeName = _nodeSessionService.GetNodeName(nodeSessionId) ?? nodeInfo.Name;
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
            ParentId = parameters.ParentTaskExecutionInstanceId,
            FireInstanceId = parameters.FireInstanceId
        };
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

                    foreach (var serviceParametersGroup in array.GroupBy(static x=>x.Parameters.Index))
                    {
                        switch (serviceParametersGroup.Key)
                        {
                            case 0:
                                await ProcessFireTaskParametersAsync(
                                    serviceParametersGroup.Select(x => x.Parameters.AsT0),
                                    cancellationToken);

                                break;
                            case 1:
                                await ProcessTaskFlowFireParametersAsync(
                                    serviceParametersGroup.Select(x => x.Parameters.AsT1),
                                    cancellationToken);
                                break;
                            case 2:
                                await ProcessRetryTaskParametersAsync(
                                    serviceParametersGroup.Select(x => x.Parameters.AsT2),
                                    cancellationToken);
                                break;
                            case 3:
                                await ProcessSwitchTaskFlowStageParametersAsync(
                                    serviceParametersGroup.Select(x => x.Parameters.AsT3),
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

    async ValueTask ProcessSwitchTaskFlowStageParametersAsync(
        IEnumerable<SwitchStageParameters> switchTaskFlowStageParameterList,
        CancellationToken cancellationToken)
    {
        foreach (var rerunTaskFlowParametersGroup in switchTaskFlowStageParameterList.GroupBy(static x => x.TaskFlowExecutionInstanceId))
        {
            var taskFlowExecutionInstanceId = rerunTaskFlowParametersGroup.Key;
            var lastRerunTaskFlowParameters = rerunTaskFlowParametersGroup.LastOrDefault();
            if (lastRerunTaskFlowParameters == default)
            {
                continue;
            }
            await _taskFlowExecutor.SwitchStageAsync(
                taskFlowExecutionInstanceId,
                lastRerunTaskFlowParameters.StageIndex,
                cancellationToken);
        }
    }

    async ValueTask ProcessRetryTaskParametersAsync(
        IEnumerable<RetryTaskParameters> retryTaskParameterList,
        CancellationToken cancellationToken = default)
    {
        await using var taskExecutionInstanceRepo = await _taskExecutionInstanceRepositoryFactory.CreateRepositoryAsync();
        await using var taskActivationRecordRepo = await _taskActivationRecordRepoFactory.CreateRepositoryAsync();
        foreach (var retryTaskFlowParametersGroup in retryTaskParameterList.GroupBy(static x => x.TaskExecutionInstanceId))
        {
            var taskExecutionInstanceId = retryTaskFlowParametersGroup.Key;
            if (taskExecutionInstanceId == null)
            {
                continue;
            }

            var taskExecutionInstance = await taskExecutionInstanceRepo.GetByIdAsync(taskExecutionInstanceId, cancellationToken);
            if (taskExecutionInstance == null)
            {
                continue;
            }
            if (!taskExecutionInstance.CanRetry())
            {
                continue;
            }

            var taskExecutionRecord = await taskActivationRecordRepo.GetByIdAsync(taskExecutionInstance.FireInstanceId, cancellationToken);
            if (taskExecutionRecord == null)
            {
                continue;
            }
            var taskDefinition = taskExecutionRecord.GetTaskDefinition();
            if (taskDefinition == null)
            {
                continue;
            }
            if (taskExecutionRecord.Value.NodeList == null)
            {
                continue;
            }
            var nodes = taskExecutionRecord.Value.NodeList.Where(x => x.Value == taskExecutionInstance.NodeInfoId).ToList();
            var fireTaskParameters = FireTaskParameters.BuildRetryTaskParameters(
                taskExecutionRecord.Id,
                taskExecutionRecord.TaskDefinitionId,
                taskExecutionInstance.FireInstanceId,
                nodes,
                taskExecutionInstance.GetTaskFlowTaskKey());
            await _serviceParametersBatchQueue.SendAsync(new TaskActivateServiceParameters(fireTaskParameters), cancellationToken);


        }
    }

    async ValueTask ProcessTaskFlowFireParametersAsync(
        IEnumerable<FireTaskFlowParameters> fireTaskFlowParameterList,
        CancellationToken cancellationToken = default)
    {


        foreach (var fireTaskFlowParametersGroup in fireTaskFlowParameterList.GroupBy(static x => x.TaskFlowTemplateId))
        {
            var taskFlowTemplateId = fireTaskFlowParametersGroup.Key;
            var taskFlowTemplate = await GetTaskFlowTemplateAsync(taskFlowTemplateId, cancellationToken);
            if (taskFlowTemplate == null)
            {
                continue;
            }
            List<TaskFlowExecutionInstanceModel> taskFlowExecutionInstances = [];
            foreach (var fireTaskFlowParameters in fireTaskFlowParametersGroup)
            {
                CreateTaskFlowExecutionInstance(taskFlowTemplate, taskFlowExecutionInstances, fireTaskFlowParameters);
            }
            foreach (var taskFlowExecutionInstance in taskFlowExecutionInstances)
            {
                await _taskFlowExecutor.ExecuteAsync(taskFlowExecutionInstance, cancellationToken);
            }
            await using var taskFlowExeuctionInstanceRepo = await _taskFlowExecutionInstanceRepoFactory.CreateRepositoryAsync();
            await taskFlowExeuctionInstanceRepo.AddRangeAsync(taskFlowExecutionInstances, cancellationToken);
        }

    }

    static void CreateTaskFlowExecutionInstance(
        TaskFlowTemplateModel? taskFlowTemplate,
        List<TaskFlowExecutionInstanceModel> taskFlowExecutionInstances,
        FireTaskFlowParameters fireTaskFlowParameters)
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
        taskFlowExecutionInstance.Value.TaskFlowTemplateId = taskFlowTemplate.Id;
        taskFlowExecutionInstance.Value.TaskFlowTemplateJson = JsonSerializer.Serialize(taskFlowTemplate);
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

    async Task ProcessFireTaskParametersAsync(
        IEnumerable<FireTaskParameters> fireTaskParameterList, 
        CancellationToken cancellationToken)
    {

        foreach (var fireTaskParameterGroup in fireTaskParameterList.GroupBy(static x => x.TaskDefinitionId))
        {
            var taskDefinitionId = fireTaskParameterGroup.Key;
            var taskDefinition = await GetTaskDefinitionAsync(
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

                taskDefinition.TaskTypeDesc = await GetTaskTypeDescAsync(taskDefinition.TaskTypeDescId, cancellationToken);
                if (taskDefinition.TaskTypeDesc == null)
                {
                    continue;
                }

                await ActivateRemoteNodeTasksAsync(
                     fireTaskParameters,
                     taskDefinition,
                     fireTaskParameters.TaskFlowTaskKey,
                     cancellationToken);
            }

        }


    }

    async ValueTask<TaskTypeDescConfigModel?> GetTaskTypeDescAsync(
        string taskTypeDescId,
        CancellationToken cancellationToken = default)
    {
        var list = await _configurationQueryService.QueryConfigurationByIdListAsync<TaskTypeDescConfigModel>([taskTypeDescId], cancellationToken);
        return list.Items?.FirstOrDefault();
    }

    async ValueTask<TaskDefinitionModel?> GetTaskDefinitionAsync(
        string taskDefinitionId,
        CancellationToken cancellationToken = default)
    {
        var list = await _configurationQueryService.QueryConfigurationByIdListAsync<TaskDefinitionModel>([taskDefinitionId], cancellationToken);
        return list.Items?.FirstOrDefault();
    }

    async ValueTask<TaskFlowTemplateModel?> GetTaskFlowTemplateAsync(
    string taskFlowTemplateId,
    CancellationToken cancellationToken = default)
    {
        var list = await _configurationQueryService.QueryConfigurationByIdListAsync<TaskFlowTemplateModel>([taskFlowTemplateId], cancellationToken);
        return list.Items?.FirstOrDefault();
    }
}