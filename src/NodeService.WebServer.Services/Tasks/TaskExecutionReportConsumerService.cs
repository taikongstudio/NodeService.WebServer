using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Logging;
using NodeService.Infrastructure.Models;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.NodeSessions;
using System.Buffers;
using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Net;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Threading;

namespace NodeService.WebServer.Services.Tasks;

public partial class TaskExecutionReportConsumerService : BackgroundService
{
    class TaskExecutionInstanceProcessContext
    {
        public TaskExecutionInstanceProcessContext(
            TaskExecutionInstanceModel? taskExecutionInstance,
            bool statusChanged,
            bool messageChanged)
        {
            TaskExecutionInstance = taskExecutionInstance;
            StatusChanged = statusChanged;
            MessageChanged = messageChanged;
        }

        public TaskExecutionInstanceModel? TaskExecutionInstance { get; set; }

        public bool StatusChanged { get; set; }

        public bool MessageChanged { get; set; }

        public IEnumerable<TaskExecutionReportMessage> Messages { get; init; }
    }

    class TaskActivationRecordProcessContext
    {
        public string FireInstanceId { get; init; }

        public TaskActivationRecordModel TaskActivationRecord { get; init; }

        public IEnumerable<TaskExecutionInstanceProcessContext> TaskExecutionInstanceProcessContexts { get; init; } = [];

        public async ValueTask ProcessAsync(CancellationToken cancellationToken = default)
        {
            if (TaskExecutionInstanceProcessContexts == null)
            {
                return;
            }

            var taskExecutionInstanceInfoList = TaskExecutionInstanceProcessContexts.Select(static x => new TaskExecutionInstanceInfo()
            {
                TaskExecutionInstanceId = x.TaskExecutionInstance.Id,
                Status = x.TaskExecutionInstance.Status,
                Message = x.TaskExecutionInstance.Message
            });
            TaskActivationRecord.Value.ResetCounters();
            for (int i = 0; i < TaskActivationRecord.TaskExecutionNodeList.Count; i++)
            {
                var taskExecutionNodeInfo = TaskActivationRecord.TaskExecutionNodeList[i];
                var nodeInfoId = taskExecutionNodeInfo.NodeInfoId;
                TaskExecutionInstanceModel? newValue = null;
                foreach (var processContext in TaskExecutionInstanceProcessContexts)
                {
                    if (processContext.TaskExecutionInstance==null)
                    {
                        continue;
                    }
                    if (processContext.TaskExecutionInstance.NodeInfoId == nodeInfoId)
                    {
                        newValue = processContext.TaskExecutionInstance;
                        break;
                    }
                }
                if (newValue != null)
                {
                    taskExecutionNodeInfo.AddOrUpdateInstance(newValue);
                }
                taskExecutionNodeInfo.UpdateCounters();
                TaskActivationRecord.TaskExecutionNodeList[i] = taskExecutionNodeInfo;
                switch (taskExecutionNodeInfo.Status)
                {
                    case TaskExecutionStatus.Unknown:
                        break;
                    case TaskExecutionStatus.Triggered:
                        TaskActivationRecord.Value.TriggeredCount++;
                        break;
                    case TaskExecutionStatus.Pendding:
                        break;
                    case TaskExecutionStatus.Started:

                        break;
                    case TaskExecutionStatus.Running:
                        TaskActivationRecord.Value.RunningCount++;
                        break;
                    case TaskExecutionStatus.Failed:
                        TaskActivationRecord.Value.FailedCount++;
                        break;
                    case TaskExecutionStatus.Finished:
                        TaskActivationRecord.Value.FinishedCount++;
                        break;
                    case TaskExecutionStatus.Cancelled:
                        TaskActivationRecord.Value.CancelledCount++;
                        break;
                    case TaskExecutionStatus.PenddingTimeout:
                        TaskActivationRecord.Value.PenddingTimeoutCount++;
                        break;
                    case TaskExecutionStatus.MaxCount:
                        break;
                    default:
                        break;
                }
            }


            if (TaskActivationRecord.FinishedCount == TaskActivationRecord.TotalCount)
            {
                TaskActivationRecord.Status = TaskExecutionStatus.Finished;
            }
            else if (TaskActivationRecord.TriggeredCount == TaskActivationRecord.TotalCount)
            {
                TaskActivationRecord.Status = TaskExecutionStatus.Triggered;
            }
            else if (TaskActivationRecord.CancelledCount > 0)
            {
                TaskActivationRecord.Status = TaskExecutionStatus.Cancelled;
            }
            else if (TaskActivationRecord.FailedCount > 0)
            {
                TaskActivationRecord.Status = TaskExecutionStatus.Failed;
            }
            else if (TaskActivationRecord.PenddingTimeoutCount > 0)
            {
                TaskActivationRecord.Status = TaskExecutionStatus.PenddingTimeout;
            }
            else if (TaskActivationRecord.RunningCount == TaskActivationRecord.TotalCount)
            {
                TaskActivationRecord.Status = TaskExecutionStatus.Running;
            }
            else if (TaskActivationRecord.RunningCount + TaskActivationRecord.FinishedCount == TaskActivationRecord.TotalCount)
            {
                TaskActivationRecord.Status = TaskExecutionStatus.Running;
            }
            TaskActivationRecord.TaskExecutionNodeList = [.. TaskActivationRecord.TaskExecutionNodeList];
            await ValueTask.CompletedTask;
        }

    }


    readonly ExceptionCounter _exceptionCounter;
    readonly TaskFlowExecutor _taskFlowExecutor;
    readonly ILogger<TaskExecutionReportConsumerService> _logger;
    readonly IMemoryCache _memoryCache;
    readonly ApplicationRepositoryFactory<TaskDefinitionModel> _taskDefinitionRepoFactory;
    readonly ApplicationRepositoryFactory<TaskExecutionInstanceModel> _taskExecutionInstanceRepoFactory;
    readonly ApplicationRepositoryFactory<TaskActivationRecordModel> _taskActivationRecordRepoFactory;
    readonly ApplicationRepositoryFactory<TaskFlowExecutionInstanceModel> _taskFlowExecutionInstanceRepoFactory;
    readonly ApplicationRepositoryFactory<TaskFlowTemplateModel> _taskFlowTemplateRepoFactory;
    readonly BatchQueue<TaskLogUnit> _taskLogUnitBatchQueue;
    readonly BatchQueue<TaskActivateServiceParameters> _taskActivateQueue;
    readonly BatchQueue<TaskCancellationParameters> _taskCancellationBatchQueue;
    readonly BatchQueue<TaskExecutionReportMessage> _taskExecutionReportBatchQueue;
    readonly BatchQueue<TaskExecutionInstanceModel> taskScheduleAsyncQueue;
    readonly JobScheduler _taskScheduler;
    readonly ConcurrentDictionary<string, IDisposable> _taskExecutionLimitDictionary;
    readonly WebServerCounter _webServerCounter;

    public TaskExecutionReportConsumerService(
        ApplicationRepositoryFactory<TaskExecutionInstanceModel> taskExecutionInstanceRepositoryFactory,
        ApplicationRepositoryFactory<TaskFlowExecutionInstanceModel> taskFlowExecutionInstanceRepoFactory,
        ApplicationRepositoryFactory<TaskDefinitionModel> taskDefinitionRepositoryFactory,
        ApplicationRepositoryFactory<TaskFlowTemplateModel> taskFlowTemplateRepoFactory,
        ApplicationRepositoryFactory<TaskActivationRecordModel> taskActivationRecordRepoFactory,
        BatchQueue<TaskExecutionReportMessage> taskExecutionReportBatchQueue,
        BatchQueue<TaskLogUnit> taskLogUnitBatchQueue,
        BatchQueue<TaskCancellationParameters> taskCancellationBatchQueue,
        BatchQueue<TaskActivateServiceParameters> taskScheduleQueue,
        ILogger<TaskExecutionReportConsumerService> logger,
        JobScheduler taskScheduler,
        IMemoryCache memoryCache,
        WebServerCounter webServerCounter,
        ExceptionCounter exceptionCounter,
        TaskFlowExecutor taskFlowExecutor
    )
    {
        _logger = logger;
        _taskExecutionInstanceRepoFactory = taskExecutionInstanceRepositoryFactory;
        _taskDefinitionRepoFactory = taskDefinitionRepositoryFactory;
        _taskActivationRecordRepoFactory = taskActivationRecordRepoFactory;
        _taskFlowExecutionInstanceRepoFactory = taskFlowExecutionInstanceRepoFactory;
        _taskFlowTemplateRepoFactory = taskFlowTemplateRepoFactory;
        _taskExecutionReportBatchQueue = taskExecutionReportBatchQueue;
        _taskActivateQueue = taskScheduleQueue;
        _taskCancellationBatchQueue = taskCancellationBatchQueue;
        _taskExecutionLimitDictionary = new ConcurrentDictionary<string, IDisposable>();
        _taskScheduler = taskScheduler;
        _taskLogUnitBatchQueue = taskLogUnitBatchQueue;
        _memoryCache = memoryCache;
        _webServerCounter = webServerCounter;
        _exceptionCounter = exceptionCounter;
        _taskFlowExecutor = taskFlowExecutor;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(
            ProcessExipredTasksAsync(cancellationToken),
            ProcessTaskExecutionReportAsync(cancellationToken));
    }

    private async Task ProcessExipredTasksAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(TimeSpan.FromMinutes(10), cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            await ProcessExpiredTaskExecutionInstanceAsync(cancellationToken);
            await Task.Delay(TimeSpan.FromHours(6), cancellationToken);
        }
    }

    private async Task ProcessTaskExecutionReportAsync(CancellationToken cancellationToken = default)
    {
        await foreach (var array in _taskExecutionReportBatchQueue.ReceiveAllAsync(cancellationToken))
        {
            if (array == null)
            {
                continue;
            }
            try
            {
                var stopwatch = new Stopwatch();
                stopwatch.Start();
                var reports = array.Select(x => x.GetMessage()).ToArray();
                await ProcessTaskExecutionReportsAsync(reports, cancellationToken);
                stopwatch.Stop();
                _logger.LogInformation(
                    $"process {array.Length} messages,spent: {stopwatch.Elapsed}, AvailableCount:{_taskExecutionReportBatchQueue.AvailableCount}");
                _webServerCounter.TaskExecutionReportAvailableCount.Value = (uint)_taskExecutionReportBatchQueue.AvailableCount;
                _webServerCounter.TaskExecutionReportTotalTimeSpan.Value += stopwatch.Elapsed;
                _webServerCounter.TaskExecutionReportConsumeCount.Value += (uint)array.Length;
                stopwatch.Reset();
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

    static string? GetTaskId(TaskExecutionReport report)
    {
        if (report == null) return null;
        if (report.Properties.TryGetValue("TaskId", out var id)) return id;
        return report.Id;
    }


    async ValueTask ProcessTaskExecutionReportsAsync(TaskExecutionReport[] array, CancellationToken cancellationToken = default)
    {
        using var taskExecutionInstanceRepo = _taskExecutionInstanceRepoFactory.CreateRepository();
        using var taskActivationRecordRepo = _taskActivationRecordRepoFactory.CreateRepository();
        var messageGroups = array.GroupBy(GetTaskId).ToArray();
        var taskExecutionInstanceIdList = Filter(messageGroups.Select(x => x.Key).Distinct()).ToArray();
        var taskExecutionInstanceIdFilters = DataFilterCollection<string>.Includes(taskExecutionInstanceIdList);
        var taskExecutionInstanceList = await taskExecutionInstanceRepo.ListAsync(
                                                                            new TaskExecutionInstanceListSpecification(taskExecutionInstanceIdFilters),
                                                                            cancellationToken);

        _webServerCounter.TaskExecutionReportQueryTimeSpan.Value += taskExecutionInstanceRepo.LastOperationTimeSpan;
        if (taskExecutionInstanceList == null)
        {
            return;
        }
        var fireInstanceIdList = Filter(taskExecutionInstanceList.GroupBy(static x => x.FireInstanceId)
                                                                 .Select(static x => x.Key)
                                                                 .Distinct()).ToArray();

        var taskActivationRecordIdFilters = DataFilterCollection<string>.Includes(fireInstanceIdList);
        var taskActivationRecords = await taskActivationRecordRepo.ListAsync(
                                                                    new ListSpecification<TaskActivationRecordModel>(taskActivationRecordIdFilters),
                                                                    cancellationToken);
        _webServerCounter.TaskExecutionReportQueryTimeSpan.Value += taskActivationRecordRepo.LastOperationTimeSpan;

        foreach (var taskExecutionInstanceGroup in taskExecutionInstanceList.GroupBy(static x => x.FireInstanceId))
        {
            var fireInstanceId = taskExecutionInstanceGroup.Key;
            if (fireInstanceId == null)
            {
                continue;
            }
            TaskActivationRecordModel? taskActivationRecord = null;
            foreach (var item in taskActivationRecords)
            {
                if (item.Id == fireInstanceId)
                {
                    taskActivationRecord = item;
                    break;
                }
            }
            if (taskActivationRecord == null)
            {
                continue;
            }
            var taskDefinition = taskActivationRecord.GetTaskDefinition();
            if (taskDefinition == null)
            {
                continue;
            }
            var processContext = await CreateTaskActivationRecordProcessContextAsync(
                taskActivationRecord,
                taskDefinition,
                taskExecutionInstanceGroup,
                messageGroups,
                cancellationToken);
            await processContext.ProcessAsync(cancellationToken);
        }

        await taskExecutionInstanceRepo.SaveChangesAsync(cancellationToken);
        await taskActivationRecordRepo.SaveChangesAsync(cancellationToken);
        _webServerCounter.TaskExecutionReportSaveTimeSpan.Value += taskExecutionInstanceRepo.LastOperationTimeSpan;
        _webServerCounter.TaskExecutionReportSaveChangesCount.Value += (uint)taskExecutionInstanceRepo.LastSaveChangesCount;

        await ProcessTaskFlowActiveRecordListAsync(taskActivationRecords, cancellationToken);

    }

    async ValueTask<TaskActivationRecordProcessContext> CreateTaskActivationRecordProcessContextAsync(
        TaskActivationRecordModel taskActivationRecord,
        TaskDefinition taskDefinition,
        IEnumerable<TaskExecutionInstanceModel> taskExecutionInstances,
        IGrouping<string?, TaskExecutionReport>[] messageGroups,
        CancellationToken cancellationToken)
    {
        List<TaskExecutionInstanceProcessContext> contexts = [];

        foreach (var taskExecutionInstance in taskExecutionInstances)
        {
            IEnumerable<TaskExecutionReport> reports = [];
            foreach (var item in messageGroups)
            {
                if (item.Key == taskExecutionInstance.Id)
                {
                    reports = item;
                    break;
                }
            }
            var context = await CreateTaskExecutionInstanceProcessContextAsync(
                taskActivationRecord,
                taskDefinition,
                taskExecutionInstance,
                 reports ?? [],
                cancellationToken);
            if (context == null)
            {
                continue;
            }
            contexts.Add(context);
        }

        var processContext = new TaskActivationRecordProcessContext()
        {
            FireInstanceId = taskActivationRecord.Id,
            TaskActivationRecord = taskActivationRecord,
            TaskExecutionInstanceProcessContexts = contexts
        };
        return processContext;
    }

    static IEnumerable<string> Filter(IEnumerable<string?> taskIdList)
    {
        if (taskIdList==null)
        {
            yield break;
        }
        foreach (var item in taskIdList)
        {
            if (item == null)
            {
                continue;
            }
            yield return item;
        }
        yield break;
    }



    async ValueTask<TaskExecutionInstanceProcessContext?> CreateTaskExecutionInstanceProcessContextAsync(
        TaskActivationRecordModel taskActivationRecord,
        TaskDefinition taskDefinition,
        TaskExecutionInstanceModel taskExecutionInstance,
        IEnumerable<TaskExecutionReport> reports,
        CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(taskActivationRecord);
            ArgumentNullException.ThrowIfNull(taskDefinition);
            ArgumentNullException.ThrowIfNull(taskExecutionInstance);
            ArgumentNullException.ThrowIfNull(reports);
            var taskId = taskExecutionInstance.Id;
            if (taskId == null) return null;
            bool statusChanged = false;
            bool messageChanged = false;

            var stopwatchCollectLogEntries = new Stopwatch();
            var stopwatchProcessMessage = new Stopwatch();

            if (taskExecutionInstance.IsTerminatedStatus()) return null;

            var logEntriesRecieveCount = taskExecutionInstance.LogEntriesRecieveCount;
            var logEntriesSaveCount = taskExecutionInstance.LogEntriesSaveCount;
            stopwatchCollectLogEntries.Restart();
            foreach (var report in reports)
            {
                if (report.LogEntries.Count > 0)
                {
                    _webServerCounter.TaskLogUnitRecieveCount.Value++;
                    var taskLogUnit = new TaskLogUnit
                    {
                        Id = taskId,
                        LogEntries = report.LogEntries
                    };
                    taskExecutionInstance.LogEntriesRecieveCount += report.LogEntries.Count;
                    await _taskLogUnitBatchQueue.SendAsync(taskLogUnit, cancellationToken);
                    _logger.LogInformation($"Send task log unit:{taskId},{report.LogEntries.Count} enties");
                }
            }

            stopwatchCollectLogEntries.Stop();
            _webServerCounter.TaskLogUnitCollectLogEntriesTimeSpan.Value += stopwatchCollectLogEntries.Elapsed;


            var taskExecutionStatus = taskExecutionInstance.Status;
            var messsage = taskExecutionInstance.Message;
            var executionBeginTime = taskExecutionInstance.ExecutionBeginTimeUtc;
            var executionEndTime = taskExecutionInstance.ExecutionEndTimeUtc;

            stopwatchProcessMessage.Restart();
            foreach (var messageStatusGroup in reports.GroupBy(static x => x.Status).OrderBy(static x => x.Key))
            {

                var status = messageStatusGroup.Key;
                foreach (var reportMessage in messageStatusGroup)
                {
                    if (reportMessage == null) continue;
                    await ProcessTaskExecutionReportAsync(
                        taskActivationRecord,
                        taskDefinition,
                        taskExecutionInstance,
                        reportMessage,
                        cancellationToken);
                }


                _logger.LogInformation($"process {status} {messageStatusGroup.Count()} messages,spent:{stopwatchProcessMessage.Elapsed}");

            }
            stopwatchProcessMessage.Stop();
            _webServerCounter.TaskExecutionReportProcessTimeSpan.Value += stopwatchProcessMessage.Elapsed;

            int diffCount = 0;

            if (logEntriesRecieveCount != taskExecutionInstance.LogEntriesRecieveCount)
            {
                diffCount++;
            }

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
            }

            if (executionEndTime != taskExecutionInstance.ExecutionEndTimeUtc)
            {
                _logger.LogInformation($"{taskId} StatusChanged:{executionEndTime}=>{taskExecutionInstance.ExecutionEndTimeUtc}");
                diffCount++;
            }
            return new TaskExecutionInstanceProcessContext(taskExecutionInstance, statusChanged, messageChanged);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
        return null;
    }

    void CancelTimeLimitTask(IEnumerable<TaskExecutionInstanceModel> taskExecutionInstances)
    {
        foreach (var item in taskExecutionInstances)
        {
            CancelTimeLimitTask(item.Id);
        }
    }

    void CancelTimeLimitTask(string taskExecutionInstanceId)
    {
        if (!_taskExecutionLimitDictionary.TryRemove(taskExecutionInstanceId, out var token)) return;
        token.Dispose();
    }

    async Task ScheduleRetryTaskAsync(
    IEnumerable<TaskExecutionInstanceModel> taskExecutionInstances,
    CancellationToken cancellationToken = default)
    {
        try
        {
            if (taskExecutionInstances == null || !taskExecutionInstances.Any()) return;
            using var taskDefinitionRepo = _taskActivationRecordRepoFactory.CreateRepository();
            foreach (var taskExecutionInstanceGroup in taskExecutionInstances.GroupBy(static x => x.FireInstanceId))
            {
                var fireInstanceId = taskExecutionInstanceGroup.Key;
                if (fireInstanceId == null) continue;
                var taskActiveRecord = await taskDefinitionRepo.GetByIdAsync(fireInstanceId, cancellationToken);
                if (taskActiveRecord == null) continue;
                var taskDefinition = JsonSerializer.Deserialize<TaskDefinition>(taskActiveRecord.Value.TaskDefinitionJson);
                if (taskDefinition == null)
                {
                    continue;
                }
                foreach (var taskExecutionInstance in taskExecutionInstanceGroup)
                {
                    if (taskActiveRecord == null || taskDefinition.ExecutionLimitTimeSeconds <= 0) continue;
                    var id = taskExecutionInstance.Id;
                    var token = Scheduler.Default.ScheduleAsync(
                         TimeSpan.FromSeconds(taskDefinition.ExecutionLimitTimeSeconds),
                         async (scheduler, cancellationToken) =>
                         {
                             try
                             {
                                 await _taskCancellationBatchQueue.SendAsync(new TaskCancellationParameters(
                                                       id,
                                                       "TaskExecutionTimeLimit",
                                                       Dns.GetHostName()));
                                 CancelTimeLimitTask(id);
                             }
                             catch (Exception ex)
                             {
                                 _exceptionCounter.AddOrUpdate(ex, taskExecutionInstance.Id);
                             }

                             return Disposable.Empty;
                         });
                    _taskExecutionLimitDictionary.AddOrUpdate(taskExecutionInstance.Id, token, (key, oldToken) =>
                    {
                        oldToken.Dispose();
                        return token;
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    private async Task ScheduleTimeLimitTaskAsync(
        IEnumerable<TaskExecutionInstanceModel> taskExecutionInstances,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (taskExecutionInstances == null || !taskExecutionInstances.Any()) return;
            using var taskActivateRecordRepo = _taskActivationRecordRepoFactory.CreateRepository();
            foreach (var taskExecutionInstanceGroup in taskExecutionInstances.GroupBy(static x => x.FireInstanceId))
            {
                var fireInstanceId = taskExecutionInstanceGroup.Key;
                if (fireInstanceId == null) continue;
                var taskActiveRecord = await taskActivateRecordRepo.GetByIdAsync(fireInstanceId, cancellationToken);
                if (taskActiveRecord == null) continue;
                var taskDefinition = JsonSerializer.Deserialize<TaskDefinition>(taskActiveRecord.Value.TaskDefinitionJson);
                if (taskDefinition == null)
                {
                    continue;
                }
                foreach (var taskExecutionInstance in taskExecutionInstanceGroup)
                {
                    ScheduleTimeLimitTask(taskDefinition, taskExecutionInstance.Id);
                }

            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    private void ScheduleTimeLimitTask(
        TaskDefinition taskDefinitionSnapshot,
        string taskExecutionInstanceId)
    {
        if (taskDefinitionSnapshot == null)
        {
            return;
        }
        if (taskDefinitionSnapshot.ExecutionLimitTimeSeconds <= 0) return;

        var dueTime = TimeSpan.FromSeconds(taskDefinitionSnapshot.ExecutionLimitTimeSeconds);
        var token = Scheduler.Default.ScheduleAsync(
            dueTime,
             async (scheduler, cancellationToken) =>
             {
                 try
                 {
                     await _taskCancellationBatchQueue.SendAsync(new TaskCancellationParameters(
                                               taskExecutionInstanceId,
                                                "TaskExecutionTimeLimit",
                                                Dns.GetHostName()));
                 }
                 catch (Exception ex)
                 {
                     _exceptionCounter.AddOrUpdate(ex, taskExecutionInstanceId);
                 }

                 return Disposable.Empty;
             });
        _taskExecutionLimitDictionary.AddOrUpdate(
            taskExecutionInstanceId,
            token,
            (key, oldToken) =>
            {
                oldToken.Dispose();
                return token;
            }
        );
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
                        ScheduleTimeLimitTask(taskDefinitionSnapshot, taskExecutionInstance.Id);
                    }
                    break;
                case TaskExecutionStatus.Running:
                    break;
                case TaskExecutionStatus.PenddingTimeout:
                case TaskExecutionStatus.Failed:
                    await RetryTaskAsync(taskActivationRecord, taskDefinitionSnapshot, taskExecutionInstance);
                    break;
                case TaskExecutionStatus.Cancelled:
                    break;
                case TaskExecutionStatus.Finished:
                    if (taskExecutionInstance.Status != TaskExecutionStatus.Finished)
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
                    CancelTimeLimitTask(taskExecutionInstance.Id);
                    break;
            }

            if (taskExecutionInstance.Status < report.Status && report.Status != TaskExecutionStatus.PenddingTimeout)
            {
                taskExecutionInstance.Status = report.Status;
            }

            if (report.Status == TaskExecutionStatus.PenddingTimeout)
            {
                taskExecutionInstance.Status = report.Status;
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

    async ValueTask RetryTaskAsync(
        TaskActivationRecordModel taskActivationRecord,
        TaskDefinition taskDefinitionSnapshot,
        TaskExecutionInstanceModel taskExecutionInstance,
        CancellationToken cancellationToken=default)
    {
        TaskExecutionNodeInfo taskExecutionNodeInfo = default;
        foreach (var item in taskActivationRecord.Value.TaskExecutionNodeList)
        {
            if (item.NodeInfoId == taskExecutionInstance.NodeInfoId)
            {
                taskExecutionNodeInfo = item;
                break;
            }
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
                fireInstanceId,
                nodeList,
                taskExecutionInstance.GetTaskFlowTaskKey());
            if (taskDefinitionSnapshot.RetryDuration == 0)
            {
                await _taskActivateQueue.SendAsync(new TaskActivateServiceParameters(fireTaskParameters), cancellationToken);
            }
            else
            {
                var dueTime = TimeSpan.FromSeconds(taskDefinitionSnapshot.RetryDuration);
                IDisposable token = Disposable.Empty;
                token = Scheduler.Default.ScheduleAsync(
                    dueTime,
                     async (scheduler, cancellationToken) =>
                     {
                         try
                         {
                             await _taskActivateQueue.SendAsync(new TaskActivateServiceParameters(fireTaskParameters), cancellationToken);
                             token.Dispose();
                         }
                         catch (Exception ex)
                         {
                             _exceptionCounter.AddOrUpdate(ex, taskExecutionInstanceId);
                         }

                         return Disposable.Empty;
                     });
            }
        }
    }

    private async Task ScheduleChildTasksAsync(
        TaskActivationRecordModel taskActivationRecord,
        TaskExecutionInstanceModel parentTaskInstance,
        CancellationToken cancellationToken = default)
    {
        if (taskActivationRecord == null)
        {
            _logger.LogError($"Could not found task fire config:{parentTaskInstance.FireInstanceId}");
            return;
        }

        using var taskDefinitionRepo = _taskDefinitionRepoFactory.CreateRepository();
        var taskDefinition = JsonSerializer.Deserialize<TaskDefinition>(taskActivationRecord.TaskDefinitionJson);
        if (taskDefinition == null) return;
        foreach (var childTaskDefinition in taskDefinition.ChildTaskDefinitions)
        {
            var childTaskScheduleDefinition = await taskDefinitionRepo.GetByIdAsync(childTaskDefinition.Value, cancellationToken);
            if (childTaskScheduleDefinition == null) continue;
            await _taskActivateQueue.SendAsync(new TaskActivateServiceParameters(new FireTaskParameters
            {
                FireTimeUtc = DateTime.UtcNow,
                TriggerSource = TriggerSource.Manual,
                FireInstanceId = $"ChildTask_{Guid.NewGuid()}",
                TaskDefinitionId = childTaskDefinition.Id,
                ScheduledFireTimeUtc = DateTime.UtcNow,
                NodeList = taskDefinition.NodeList,
                ParentTaskId = parentTaskInstance.Id
            }), cancellationToken);
            parentTaskInstance.ChildTaskScheduleCount++;
        }
    }

    private async ValueTask CancelChildTasksAsync(
         TaskExecutionInstanceModel parentTaskInstance,
        CancellationToken cancellationToken = default)
    {
        if (parentTaskInstance.ChildTaskScheduleCount == 0)
        {
            return;
        }
        using var taskActivationRecordRepo = _taskActivationRecordRepoFactory.CreateRepository();
        var taskActivationRecord =
            await taskActivationRecordRepo.GetByIdAsync(parentTaskInstance.FireInstanceId, cancellationToken);

        if (taskActivationRecord == null)
        {
            _logger.LogError($"Could not found task fire config:{parentTaskInstance.FireInstanceId}");
            return;
        }

        using var taskDefinitionRepo = _taskDefinitionRepoFactory.CreateRepository();
        using var taskExecutionInstanceRepo = _taskExecutionInstanceRepoFactory.CreateRepository();
        var taskDefinition = JsonSerializer.Deserialize<TaskDefinition>(taskActivationRecord.TaskDefinitionJson);
        if (taskDefinition == null) return;

        foreach (var childTaskDefinition in taskDefinition.ChildTaskDefinitions)
        {
            var childTaskScheduleDefinition =
                await taskDefinitionRepo.GetByIdAsync(childTaskDefinition.Value, cancellationToken);
            if (childTaskScheduleDefinition == null)
            {
                continue;
            }

            var childTaskExectionInstances = await taskExecutionInstanceRepo.ListAsync(
                new TaskExecutionInstanceListSpecification(
                    DataFilterCollection<TaskExecutionStatus>.Includes(
                    [
                        TaskExecutionStatus.Triggered,
                        TaskExecutionStatus.Running
                    ]),
                    DataFilterCollection<string>.Includes([parentTaskInstance.Id]),
                    DataFilterCollection<string>.Includes([childTaskDefinition.Id])
                    ), cancellationToken);
            if (childTaskExectionInstances.Count == 0) continue;

            foreach (var childTaskExectionInstance in childTaskExectionInstances)
            {
                await _taskCancellationBatchQueue.SendAsync(new TaskCancellationParameters(
                    childTaskExectionInstance.Id,
                    nameof(CancelChildTasksAsync),
                    Dns.GetHostName()), cancellationToken);
                await CancelChildTasksAsync(childTaskExectionInstance, cancellationToken);
            }

        }
    }

    async ValueTask ProcessExpiredTaskExecutionInstanceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var taskExecutionInstanceRepo = _taskExecutionInstanceRepoFactory.CreateRepository();
            using var taskDefinitionRepo = _taskDefinitionRepoFactory.CreateRepository();
            var pageIndex = 1;
            var pageSize = 100;
            while (true)
            {
                var listQueryResult = await taskExecutionInstanceRepo.PaginationQueryAsync(
                                            new TaskExecutionInstanceListSpecification(
                                                DataFilterCollection<TaskExecutionStatus>.Includes(
                                                [
                                                    TaskExecutionStatus.Triggered,
                                                    TaskExecutionStatus.Started,
                                                    TaskExecutionStatus.Running
                                                ]),
                                                false),
                                            new PaginationInfo(pageIndex, pageSize),
                                            cancellationToken);

                var taskExeuctionInstances = listQueryResult.Items;

                await ProcessTaskExecutionInstanceAsync(taskDefinitionRepo, taskExeuctionInstances, cancellationToken);
                if (listQueryResult.Items.Count() < pageSize)
                {
                    break;
                }
                pageIndex++;
            }

        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    private async Task ProcessTaskExecutionInstanceAsync(
        IRepository<TaskDefinitionModel> taskDefinitionRepo,
        IEnumerable<TaskExecutionInstanceModel> taskExeuctionInstances,
        CancellationToken cancellationToken = default)
    {
        List<TaskExecutionInstanceModel> taskExecutionInstanceList = [];
        foreach (var taskExecutionInstanceGroup in taskExeuctionInstances.GroupBy(static x => x.TaskDefinitionId))
        {
            var taskDefinitionId = taskExecutionInstanceGroup.Key;
            if (taskDefinitionId == null) continue;
            var taskDefinition = await taskDefinitionRepo.GetByIdAsync(taskDefinitionId, cancellationToken);
            if (taskDefinition == null) continue;
            taskExecutionInstanceList.Clear();
            foreach (var taskExecutionInstance in taskExecutionInstanceGroup)
            {
                if (taskExecutionInstance.FireTimeUtc == DateTime.MinValue)
                {
                    continue;
                }

                if (DateTime.UtcNow - taskExecutionInstance.FireTimeUtc < TimeSpan.FromDays(7)) continue;
                if (taskExecutionInstance.FireTimeUtc + TimeSpan.FromSeconds(taskDefinition.ExecutionLimitTimeSeconds) < DateTime.UtcNow)
                {
                    await _taskCancellationBatchQueue.SendAsync(new TaskCancellationParameters(
                        taskExecutionInstance.Id,
                        nameof(TaskExecutionReportConsumerService),
                        Dns.GetHostName()), cancellationToken);
                    await CancelChildTasksAsync(taskExecutionInstance, cancellationToken);
                }
                else
                {
                    taskExecutionInstanceList.Add(taskExecutionInstance);
                }
            }

            if (taskExecutionInstanceList.Count > 0)
            {
                await ScheduleTimeLimitTaskAsync(taskExecutionInstanceList, cancellationToken);
            }
        }
    }

}