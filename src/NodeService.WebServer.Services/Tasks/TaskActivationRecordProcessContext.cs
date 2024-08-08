using Microsoft.Extensions.DependencyInjection;
using NodeService.Infrastructure.DataModels;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.TaskSchedule;
using System.Collections.Immutable;

namespace NodeService.WebServer.Services.Tasks;

internal partial class TaskActivationRecordProcessContext
{

    readonly WebServerCounter _webServerCounter;
    readonly BatchQueue<TaskLogUnit> _taskLogUnitBatchQueue;
    readonly ExceptionCounter _exceptionCounter;
    readonly ILogger<TaskActivationRecordProcessContext> _logger;
    private readonly IMemoryCache _memoryCache;
    readonly IAsyncQueue<KafkaDelayMessage> _delayMessageQueue;
    readonly ApplicationRepositoryFactory<TaskExecutionInstanceModel> _taskExecutionInstanceRepoFactory;
    readonly ApplicationRepositoryFactory<TaskFlowExecutionInstanceModel> _taskFlowExecutionInstanceRepoFactory;
    readonly ApplicationRepositoryFactory<TaskFlowTemplateModel> _taskFlowTemplateRepoFactory;
    readonly ApplicationRepositoryFactory<TaskActivationRecordModel> _taskActivationRecordRepoFactory;
    readonly BatchQueue<TaskActivateServiceParameters> _taskActivationQueue;
    private readonly BatchQueue<TaskCancellationParameters> _taskCancellationBatchQueue;
    private readonly IAsyncQueue<TaskObservationEvent> _taskObservationEventQueue;
    private readonly TaskObservationConfiguration _taskObservationConfiguration;
    private readonly IComparer<TaskExecutionStatus> _taskExecutionStatusComparer;

    public TaskActivationRecordProcessContext(
        ILogger<TaskActivationRecordProcessContext> logger,
        WebServerCounter webServerCounter,
        ExceptionCounter exceptionCounter,
        IMemoryCache memoryCache,
        IAsyncQueue<KafkaDelayMessage> delayMessageQueue,
        BatchQueue<TaskActivateServiceParameters> taskActivateQueue,
        BatchQueue<TaskCancellationParameters> taskCancellationBatchQueue,
        [FromKeyedServices(nameof(TaskLogKafkaProducerService))] BatchQueue<TaskLogUnit> taskLogUnitBatchQueue,
        ApplicationRepositoryFactory<TaskExecutionInstanceModel> taskExecutionInstanceRepositoryFactory,
        ApplicationRepositoryFactory<TaskFlowExecutionInstanceModel> taskFlowExecutionInstanceRepoFactory,
        ApplicationRepositoryFactory<TaskFlowTemplateModel> taskFlowTemplateRepoFactory,
        ApplicationRepositoryFactory<TaskActivationRecordModel> taskActivationRecordRepoFactory,
        IAsyncQueue<TaskObservationEvent> taskObservationEventQueue,
        string taskActivationRecordId,
        IEnumerable<TaskExecutionInstanceProcessContext> processContexts,
        TaskObservationConfiguration taskObservationConfiguration
        )
    {
        TaskActivationRecordId = taskActivationRecordId;
        ProcessContexts = processContexts;
        _webServerCounter = webServerCounter;
        _taskLogUnitBatchQueue = taskLogUnitBatchQueue;
        _exceptionCounter = exceptionCounter;
        _logger = logger;
        _memoryCache = memoryCache;
        _delayMessageQueue = delayMessageQueue;
        _taskExecutionInstanceRepoFactory = taskExecutionInstanceRepositoryFactory;
        _taskFlowExecutionInstanceRepoFactory = taskFlowExecutionInstanceRepoFactory;
        _taskFlowTemplateRepoFactory = taskFlowTemplateRepoFactory;
        _taskActivationRecordRepoFactory = taskActivationRecordRepoFactory;
        _taskActivationQueue = taskActivateQueue;
        _taskCancellationBatchQueue = taskCancellationBatchQueue;
        _taskObservationEventQueue = taskObservationEventQueue;
        _taskObservationConfiguration = taskObservationConfiguration;
        _taskExecutionStatusComparer = new TaskExecutionStatusComparer();
    }

    public string TaskActivationRecordId { get; set; }

    public TaskActivationRecordModel? TaskActivationRecord { get; set; }

    public IEnumerable<TaskExecutionInstanceProcessContext> ProcessContexts { get; init; } = [];

    public bool HasChanged { get; private set; }

    public async ValueTask ProcessAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            TaskActivationRecord = await QueryTaskActiveRecordAsync(this.TaskActivationRecordId, cancellationToken);
            if (TaskActivationRecord == null)
            {
                return;
            }
            var taskDefinition = TaskActivationRecord.GetTaskDefinition();
            if (taskDefinition == null)
            {
                return;
            }

            if (ProcessContexts == null)
            {
                return;
            }
            var changesCount = 0;
            foreach (var processContext in ProcessContexts)
            {
                if (!await ProcessContextAsync(
                    TaskActivationRecord,
                    taskDefinition,
                    processContext,
                    cancellationToken))
                {
                    continue;
                }
                if (processContext.StatusChanged || processContext.MessageChanged || processContext.BeginTimeChanged || processContext.EndTimeChanged)
                {
                    changesCount++;
                }
            }

            if (changesCount == 0)
            {
                return;
            }

            TaskActivationRecord.Value.ResetCounters();

            foreach (var taskExecutionNodeInfo in TaskActivationRecord.TaskExecutionNodeList)
            {
                var nodeInfoId = taskExecutionNodeInfo.NodeInfoId;
                TaskExecutionInstanceModel? newValue = null;
                foreach (var processContext in ProcessContexts)
                {
                    if (processContext.Instance == null)
                    {
                        continue;
                    }
                    if (processContext.Instance.NodeInfoId == nodeInfoId)
                    {
                        newValue = processContext.Instance;
                        break;
                    }
                }
                if (newValue != null)
                {
                    taskExecutionNodeInfo.AddOrUpdateInstance(newValue);
                }
                taskExecutionNodeInfo.UpdateCounters();
                switch (taskExecutionNodeInfo.Status)
                {
                    case TaskExecutionStatus.Unknown:
                        break;
                    case TaskExecutionStatus.Triggered:
                        TaskActivationRecord.TriggeredCount++;
                        break;
                    case TaskExecutionStatus.Pendding:
                        TaskActivationRecord.PenddingCount++;
                        break;
                    case TaskExecutionStatus.Started:
                        TaskActivationRecord.StartedCount++;
                        break;
                    case TaskExecutionStatus.Running:
                        TaskActivationRecord.RunningCount++;
                        break;
                    case TaskExecutionStatus.Failed:
                        TaskActivationRecord.FailedCount++;
                        break;
                    case TaskExecutionStatus.Finished:
                        TaskActivationRecord.FinishedCount++;
                        break;
                    case TaskExecutionStatus.Cancelled:
                        TaskActivationRecord.CancelledCount++;
                        break;
                    case TaskExecutionStatus.PenddingTimeout:
                        TaskActivationRecord.PenddingTimeoutCount++;
                        break;
                    case TaskExecutionStatus.MaxCount:
                        break;
                    default:
                        break;
                }
            }

            var totalCount = TaskActivationRecord.TotalCount;
            var failedCount = TaskActivationRecord.FailedCount;
            var runningCount = TaskActivationRecord.RunningCount;
            var canceledCount = TaskActivationRecord.CancelledCount;
            var triggeredCount = TaskActivationRecord.TriggeredCount;
            var penddingCount = TaskActivationRecord.PenddingCount;
            var pennddingTimeoutCount = TaskActivationRecord.PenddingTimeoutCount;
            var finishedCount = TaskActivationRecord.FinishedCount;


            if (finishedCount == totalCount)
            {
                TaskActivationRecord.Status = TaskExecutionStatus.Finished;
            }
            else if (failedCount == totalCount)
            {
                TaskActivationRecord.Status = TaskExecutionStatus.Failed;
            }
            else if (canceledCount == totalCount)
            {
                TaskActivationRecord.Status = TaskExecutionStatus.Cancelled;
            }
            else if (pennddingTimeoutCount == totalCount)
            {
                TaskActivationRecord.Status = TaskExecutionStatus.PenddingTimeout;
            }
            else if (runningCount > 0)
            {
                TaskActivationRecord.Status = TaskExecutionStatus.Running;
            }
            else if (runningCount == 0 && (failedCount > 0 || canceledCount > 0 || pennddingTimeoutCount > 0))
            {
                TaskActivationRecord.Status = TaskExecutionStatus.Failed;
            }
            else if (triggeredCount == totalCount)
            {
                TaskActivationRecord.Status = TaskExecutionStatus.Triggered;
            }
            else if (penddingCount == totalCount)
            {
                TaskActivationRecord.Status = TaskExecutionStatus.Pendding;
            }

            TaskActivationRecord.TaskExecutionNodeList = [.. TaskActivationRecord.TaskExecutionNodeList.Select(x => x with { })];

            await SaveTaskExecutionInstanceListAsync(
                this.ProcessContexts.Select(static x => x.Instance),
                cancellationToken);
            await SaveTaskActivationRecordAsync([TaskActivationRecord], cancellationToken);
            HasChanged = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
            _exceptionCounter.AddOrUpdate(ex, TaskActivationRecordId);
        }

    }

    async ValueTask<bool> ProcessContextAsync(
    TaskActivationRecordModel taskActivationRecord,
    TaskDefinition taskDefinition,
    TaskExecutionInstanceProcessContext processContext,
    CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(processContext);
            var taskExecutionInstance = processContext.Instance;
            if (taskExecutionInstance == null) return false;
            var taskId = taskExecutionInstance.Id;
            if (taskId == null) return false;
            bool statusChanged = false;
            bool messageChanged = false;
            bool beginTimeChanged = false;
            bool endTimeChanged = false;

            var stopwatchCollectLogEntries = new Stopwatch();
            var stopwatchProcessMessage = new Stopwatch();

            if (taskExecutionInstance.IsTerminatedStatus()) return false;

            _webServerCounter.TaskLogUnitCollectLogEntriesTimeSpan.Value += stopwatchCollectLogEntries.Elapsed;


            var taskExecutionStatus = taskExecutionInstance.Status;
            var messsage = taskExecutionInstance.Message;
            var executionBeginTime = taskExecutionInstance.ExecutionBeginTimeUtc;
            var executionEndTime = taskExecutionInstance.ExecutionEndTimeUtc;

            stopwatchProcessMessage.Restart();
            foreach (var reportStatusGroup in processContext.Reports.GroupBy(static x => x.Status).OrderBy(static x => x.Key, _taskExecutionStatusComparer))
            {

                var status = reportStatusGroup.Key;
                foreach (var report in reportStatusGroup)
                {
                    if (report == null) continue;
                    await CreateTasKObservationEventAsync(
                        taskExecutionInstance,
                        report,
                        cancellationToken);
                    await ProcessTaskExecutionReportAsync(
                        taskActivationRecord,
                        taskDefinition,
                        taskExecutionInstance,
                        report,
                        cancellationToken);
                }


                _logger.LogInformation($"process {status} {reportStatusGroup.Count()} messages,spent:{stopwatchProcessMessage.Elapsed}");

            }
            stopwatchProcessMessage.Stop();
            _webServerCounter.TaskExecutionReportProcessTimeSpan.Value += stopwatchProcessMessage.Elapsed;

            int diffCount = 0;

            if (taskExecutionStatus != taskExecutionInstance.Status)
            {
                statusChanged = true;
                _logger.LogInformation($"{taskId} StatusChanged:{taskExecutionStatus}=>{taskExecutionInstance.Status}");
                diffCount++;
            }

            if (messsage != taskExecutionInstance.Message)
            {
                messageChanged = true;
                _logger.LogInformation($"{taskId} StatusChanged:{messsage}=>{taskExecutionInstance.Message}");
                messsage = taskExecutionInstance.Message;
                diffCount++;
            }
            
            if (executionBeginTime != taskExecutionInstance.ExecutionBeginTimeUtc)
            {
                _logger.LogInformation($"{taskId} StatusChanged:{executionBeginTime}=>{taskExecutionInstance.ExecutionBeginTimeUtc}");
                diffCount++;
                beginTimeChanged = true;
            }

            if (executionEndTime != taskExecutionInstance.ExecutionEndTimeUtc)
            {
                _logger.LogInformation($"{taskId} StatusChanged:{executionEndTime}=>{taskExecutionInstance.ExecutionEndTimeUtc}");
                diffCount++;
                endTimeChanged = true;
            }
            processContext.MessageChanged = messageChanged;
            processContext.StatusChanged = statusChanged;
            processContext.BeginTimeChanged = beginTimeChanged;
            processContext.EndTimeChanged = endTimeChanged;
            return true;
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
        return false;
    }

    private async ValueTask CreateTasKObservationEventAsync(TaskExecutionInstanceModel taskExecutionInstance, TaskExecutionReport report, CancellationToken cancellationToken)
    {
        if (taskExecutionInstance.Status != report.Status)
        {
            if (_taskObservationConfiguration != null && _taskObservationConfiguration.TaskObservations != null)
            {
                foreach (var item in _taskObservationConfiguration.TaskObservations)
                {
                    if (!item.IsEnabled)
                    {
                        continue;
                    }
                    if (item.Status != report.Status)
                    {
                        continue;
                    }
                    await _taskObservationEventQueue.EnqueueAsync(new TaskObservationEvent()
                    {
                        Id = taskExecutionInstance.Id,
                        Name = taskExecutionInstance.Name,
                        Status = (int)report.Status,
                        Context = taskExecutionInstance.NodeInfoId,
                        Message = report.Message,
                        Type = nameof(TaskExecutionInstanceModel),
                        CreationDateTime = DateTime.UtcNow,
                    }, cancellationToken);
                }
            }
        }
    }

    async ValueTask ProcessTaskExecutionReportAsync(
   TaskActivationRecordModel taskActivationRecord,
   TaskDefinition taskDefinitionSnapshot,
   TaskExecutionInstanceModel taskExecutionInstance,
   TaskExecutionReport report,
   CancellationToken cancellationToken = default)
    {
        try
        {

            switch (report.Status)
            {
                case TaskExecutionStatus.Unknown:
                    break;
                case TaskExecutionStatus.Triggered:
                    break;
                case TaskExecutionStatus.Pendding:
                    break;
                case TaskExecutionStatus.Started:
                    if (taskExecutionInstance.Status != TaskExecutionStatus.Started)
                    {
                        await ScheduleTimeLimitTask(taskDefinitionSnapshot, taskExecutionInstance.Id);
                    }
                    break;
                case TaskExecutionStatus.Running:
                    break;
                case TaskExecutionStatus.PenddingTimeout:
                case TaskExecutionStatus.Failed:
                    await RetryTaskAsync(
                        taskActivationRecord,
                        taskDefinitionSnapshot,
                        taskExecutionInstance,
                        cancellationToken);
                    break;
                case TaskExecutionStatus.Cancelled:
                    break;
                case TaskExecutionStatus.Finished:
                    if (taskExecutionInstance.Status != TaskExecutionStatus.Finished && taskExecutionInstance.TaskFlowTaskId == null)
                    {
                        await ScheduleChildTasksAsync(
                            taskActivationRecord,
                            taskExecutionInstance,
                            cancellationToken);
                    }
                    break;
            }


            switch (report.Status)
            {
                case TaskExecutionStatus.Unknown:
                    break;
                case TaskExecutionStatus.Triggered:
                case TaskExecutionStatus.Pendding:
                    break;
                case TaskExecutionStatus.Started:
                    taskExecutionInstance.ExecutionBeginTimeUtc = DateTime.UtcNow;
                    break;
                case TaskExecutionStatus.Running:
                    break;
                case TaskExecutionStatus.Failed:
                case TaskExecutionStatus.Finished:
                case TaskExecutionStatus.Cancelled:
                    taskExecutionInstance.ExecutionEndTimeUtc = DateTime.UtcNow;
                    break;
                case TaskExecutionStatus.PenddingCancel:
                    break;
            }

            if (report.Status == TaskExecutionStatus.PenddingCancel)
            {
                taskExecutionInstance.Status = TaskExecutionStatus.PenddingCancel;
            }
            else if (taskExecutionInstance.Status == TaskExecutionStatus.PenddingCancel && report.Status == TaskExecutionStatus.Cancelled)
            {
                taskExecutionInstance.Status = TaskExecutionStatus.Cancelled;
            }
            else
            {
                if (taskExecutionInstance.Status < report.Status && report.Status != TaskExecutionStatus.PenddingTimeout)
                {
                    taskExecutionInstance.Status = report.Status;
                }
                else if (report.Status == TaskExecutionStatus.PenddingTimeout)
                {
                    taskExecutionInstance.Status = report.Status;
                }
            }

            if (!string.IsNullOrEmpty(report.Message))
            {

                if (report.Status == TaskExecutionStatus.Cancelled)
                {
                    var key = $"{nameof(TaskCancellationQueueService)}:{taskExecutionInstance.Id}";
                    if (_memoryCache.TryGetValue<TaskCancellationParameters>(key, out var parameters) && parameters != null)
                    {
                        _memoryCache.Remove(key);
                        taskExecutionInstance.Message = $"{nameof(parameters.Source)}: {parameters.Source},{nameof(parameters.Context)}: {parameters.Context}";
                    }
                }
                else
                {
                    taskExecutionInstance.Message = report.Message;
                }
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    private async ValueTask ScheduleTimeLimitTask(
    TaskDefinition taskDefinitionSnapshot,
    string taskExecutionInstanceId)
    {
        if (taskDefinitionSnapshot == null)
        {
            return;
        }
        if (taskDefinitionSnapshot.ExecutionLimitTimeSeconds <= 0) return;

        var dueTime = TimeSpan.FromSeconds(taskDefinitionSnapshot.ExecutionLimitTimeSeconds);
        await _delayMessageQueue.EnqueueAsync(new KafkaDelayMessage()
        {
            Type = nameof(TaskExecutionReportConsumerService),
            SubType = TaskExecutionReportConsumerService.SubType_ExecutionTimeLimit,
            CreateDateTime = DateTime.UtcNow,
            ScheduleDateTime = DateTime.UtcNow + dueTime,
            Id = taskExecutionInstanceId,
        });
    }

    async ValueTask ScheduleChildTasksAsync(
        TaskActivationRecordModel taskActivationRecord,
        TaskExecutionInstanceModel parentTaskInstance,
        CancellationToken cancellationToken = default)
    {
        if (taskActivationRecord == null)
        {
            _logger.LogError($"Could not found task fire config:{parentTaskInstance.FireInstanceId}");
            return;
        }

        await using var taskActivationRecordRepo = await _taskActivationRecordRepoFactory.CreateRepositoryAsync(cancellationToken);
        var taskDefinition = JsonSerializer.Deserialize<TaskDefinition>(taskActivationRecord.TaskDefinitionJson);
        if (taskDefinition == null) return;
        foreach (var childTaskDefinition in taskDefinition.ChildTaskDefinitions)
        {
            var childTaskScheduleDefinition = await taskActivationRecordRepo.GetByIdAsync(childTaskDefinition.Value, cancellationToken);
            if (childTaskScheduleDefinition == null) continue;
            await _taskActivationQueue.SendAsync(new TaskActivateServiceParameters(new FireTaskParameters
            {
                FireTimeUtc = DateTime.UtcNow,
                TriggerSource = TriggerSource.Manual,
                TaskActivationRecordId = $"ChildTask_{Guid.NewGuid()}",
                TaskDefinitionId = childTaskDefinition.Id,
                ScheduledFireTimeUtc = DateTime.UtcNow,
                NodeList = [.. taskDefinition.NodeList],
                ParentTaskExecutionInstanceId = parentTaskInstance.Id
            }), cancellationToken);
            parentTaskInstance.ChildTaskScheduleCount++;
        }
    }


    async ValueTask RetryTaskAsync(
        TaskActivationRecordModel taskActivationRecord,
        TaskDefinition taskDefinitionSnapshot,
        TaskExecutionInstanceModel taskExecutionInstance,
        CancellationToken cancellationToken = default)
    {
        if (taskActivationRecord.TaskFlowInstanceId != null)
        {
            return;
        }
        TaskExecutionNodeInfo? taskExecutionNodeInfo = default;
        foreach (var item in taskActivationRecord.Value.TaskExecutionNodeList)
        {
            if (item.NodeInfoId == taskExecutionInstance.NodeInfoId)
            {
                taskExecutionNodeInfo = item;
                break;
            }
        }
        if (taskExecutionNodeInfo == null)
        {
            return;
        }
        if (taskDefinitionSnapshot.MaxRetryCount > 0 && taskExecutionNodeInfo.RetryCount < taskDefinitionSnapshot.MaxRetryCount - 1)
        {
            var taskActiveRecordId = taskActivationRecord.Id;
            var taskExecutionInstanceId = taskExecutionInstance.Id;
            var taskDefinitionId = taskExecutionInstance.TaskDefinitionId;
            var fireInstanceId = taskExecutionInstance.FireInstanceId;
            var nodeInfoId = taskExecutionInstance.NodeInfoId;
            var nodeList = taskActivationRecord.NodeList.Where(x => x.Value == nodeInfoId).ToList();
            var fireTaskParameters = FireTaskParameters.BuildRetryTaskParameters(
                taskActiveRecordId,
                taskDefinitionId,
                nodeList.ToImmutableArray(),
                taskActivationRecord.GetTaskDefinition()?.EnvironmentVariables.ToImmutableArray() ?? [],
                taskExecutionInstance.GetTaskFlowTaskKey());
            if (taskDefinitionSnapshot.RetryDuration == 0)
            {
                await _taskActivationQueue.SendAsync(new TaskActivateServiceParameters(fireTaskParameters), cancellationToken);
            }
            else
            {
                var dueTime = TimeSpan.FromSeconds(taskDefinitionSnapshot.RetryDuration);
                var delayMessage = new KafkaDelayMessage()
                {
                    Type = nameof(TaskExecutionReportConsumerService),
                    SubType = TaskExecutionReportConsumerService.SubType_Retry,
                    CreateDateTime = DateTime.UtcNow,
                    ScheduleDateTime = DateTime.UtcNow + dueTime,
                    Id = taskExecutionInstanceId,
                };
                delayMessage.Properties.Add(nameof(FireTaskParameters), JsonSerializer.Serialize(fireTaskParameters));
                await _delayMessageQueue.EnqueueAsync(delayMessage, cancellationToken);
            }
        }

    }


    async ValueTask<TaskActivationRecordModel?> QueryTaskActiveRecordAsync(
        string fireInstanceId,
        CancellationToken cancellationToken = default)
    {
        await using var taskActiveRecordRepo = await _taskActivationRecordRepoFactory.CreateRepositoryAsync(cancellationToken);
        var taskActiveRecord = await taskActiveRecordRepo.GetByIdAsync(fireInstanceId, cancellationToken);
        return taskActiveRecord;
    }

    async ValueTask SaveTaskActivationRecordAsync(
    IEnumerable<TaskActivationRecordModel> taskActivationRecordList,
    CancellationToken cancellationToken = default)
    {
        await using var taskActivationRecordRepo = await _taskActivationRecordRepoFactory.CreateRepositoryAsync(cancellationToken);
        await taskActivationRecordRepo.UpdateRangeAsync(taskActivationRecordList, cancellationToken);
        var taskActivationRecordUpdateCount = taskActivationRecordRepo.LastSaveChangesCount;
        _webServerCounter.TaskExecutionReportSaveTimeSpan.Value += taskActivationRecordRepo.LastOperationTimeSpan;
        _webServerCounter.TaskExecutionReportSaveChangesCount.Value += (uint)taskActivationRecordRepo.LastSaveChangesCount;
    }

    async ValueTask SaveTaskExecutionInstanceListAsync(
        IEnumerable<TaskExecutionInstanceModel> taskExecutionInstanceList,
        CancellationToken cancellationToken = default)
    {
        await using var taskExecutionInstanceRepo = await _taskExecutionInstanceRepoFactory.CreateRepositoryAsync(cancellationToken);
        await taskExecutionInstanceRepo.UpdateRangeAsync(taskExecutionInstanceList, cancellationToken);
        var taskExecutionInstanceUpdateCount = taskExecutionInstanceRepo.LastSaveChangesCount;
    }

}