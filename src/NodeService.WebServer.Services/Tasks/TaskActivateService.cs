using MathNet.Numerics.Statistics;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Models;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.NodeSessions;
using OneOf;
using System.Collections.Immutable;
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
        List<StringEntry> envVars,
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
            EnvironmentVariables = envVars,
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

    public List<StringEntry> EnvironmentVariables { get; init; }
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
    readonly TaskActivationRecordExecutor _taskActivationRecordExecutionQueue;


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
        ConfigurationQueryService configurationQueryService,
        TaskActivationRecordExecutor taskActivationRecordExecutot)
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
        _taskActivationRecordExecutionQueue = taskActivationRecordExecutot;
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
                    _priorityQueue.Enqueue(penddingContext, penddingContext.TaskActivationRecord.GetTaskDefinition().Priority);
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
        var penddingLimitTimeSeconds = 0;
        try
        {
            var taskDefinition = context.TaskActivationRecord.GetTaskDefinition();
            if (taskDefinition == null)
            {
                return;
            }
            penddingLimitTimeSeconds = taskDefinition.PenddingLimitTimeSeconds;
            if (!context.CancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation($"{context.Id}:Start init");
                switch (taskDefinition.ExecutionStrategy)
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
                $"Time out after waiting {penddingLimitTimeSeconds} seconds");
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


    async ValueTask ProcessTaskActivationRecordResultAsync(
TaskActivationRecordResult result,
        CancellationToken cancellationToken = default)
    {
        var taskDefinition = result.TaskActivationRecord.GetTaskDefinition();
        foreach (var kv in result.Instances)
        {
            var taskExecutionInstance = kv.Value;
            if (taskDefinition.TaskTypeDesc.Value.FullName == "NodeService.ServiceHost.Tasks.ExecuteBatchScriptTask")
            {
                if (taskDefinition.Options.TryGetValue("Scripts", out var scriptsObject) && scriptsObject is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String)
                {
                    var scriptsString = jsonElement.GetString();
                    foreach (var item in taskDefinition.EnvironmentVariables)
                    {
                        scriptsString = scriptsString.Replace($"$({item.Name})", item.Value);
                    }
                    taskDefinition.Options["Scripts"] = scriptsString;
                }
            }
            var context = new TaskPenddingContext(taskExecutionInstance.Id, result.TaskActivationRecord, taskDefinition)
            {
                NodeSessionService = _nodeSessionService,
                NodeSessionId = new NodeSessionId(taskExecutionInstance.NodeInfoId),
                TriggerEvent = taskExecutionInstance.ToTriggerEvent(new TaskDefinitionModel()
                {
                    Value = taskDefinition,
                    Id = result.TaskActivationRecord.TaskDefinitionId,
                }, result.FireTaskParameters.EnvironmentVariables),
                FireParameters = result.FireTaskParameters,
            };

            await SendTaskExecutionReportAsync(taskExecutionInstance.Id, TaskExecutionStatus.Triggered, string.Empty);
            if (_taskPenddingContextManager.AddContext(context))
            {
                context.EnsureInit();
                await PenddingContextChannel.Writer.WriteAsync(context, cancellationToken);
            }

        }
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
                taskExecutionRecord.Value.EnvironmentVariables,
                taskExecutionInstance.GetTaskFlowTaskKey());
            await _serviceParametersBatchQueue.SendAsync(new TaskActivateServiceParameters(fireTaskParameters), cancellationToken);


        }
    }

    async ValueTask ProcessTaskFlowFireParametersAsync(
        IEnumerable<FireTaskFlowParameters> fireTaskFlowParameterList,
        CancellationToken cancellationToken = default)
    {
        foreach (var fireTaskFlowParameters in fireTaskFlowParameterList)
        {
            await _taskFlowExecutor.CreateAsync(fireTaskFlowParameters, cancellationToken);
        }
    }



    async Task ProcessFireTaskParametersAsync(
        IEnumerable<FireTaskParameters> fireTaskParameterList,
        CancellationToken cancellationToken)
    {

        foreach (var fireTaskParameters in fireTaskParameterList)
        {
            var result = await _taskActivationRecordExecutionQueue.CreateTaskActiveRecordAsync(fireTaskParameters, cancellationToken);
            if (result.Index == 1)
            {
                continue;
            }
            await ProcessTaskActivationRecordResultAsync(result.AsT0, cancellationToken);
        }
    }

}