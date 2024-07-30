using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.Logging;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.NodeSessions;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;

namespace NodeService.WebServer.Services.Tasks;

public partial class TaskExecutionReportConsumerService : BackgroundService
{
    readonly ExceptionCounter _exceptionCounter;
    readonly TaskFlowExecutor _taskFlowExecutor;
    readonly ConfigurationQueryService _configurationQueryService;
    readonly ITaskPenddingContextManager _taskPenddingContextManager;
    readonly ILogger<TaskExecutionReportConsumerService> _logger;
    readonly IMemoryCache _memoryCache;

    readonly ApplicationRepositoryFactory<TaskExecutionInstanceModel> _taskExecutionInstanceRepoFactory;
    readonly ApplicationRepositoryFactory<TaskActivationRecordModel> _taskActivationRecordRepoFactory;
    readonly ApplicationRepositoryFactory<TaskFlowExecutionInstanceModel> _taskFlowExecutionInstanceRepoFactory;
    readonly ApplicationRepositoryFactory<TaskFlowTemplateModel> _taskFlowTemplateRepoFactory;
    readonly BatchQueue<TaskLogUnit> _taskLogUnitBatchQueue;
    readonly BatchQueue<TaskActivateServiceParameters> _taskActivateQueue;
    readonly BatchQueue<TaskCancellationParameters> _taskCancellationBatchQueue;
    readonly BatchQueue<TaskExecutionReportMessage> _taskExecutionReportBatchQueue;
    readonly JobScheduler _jobScheduler;
    readonly ConcurrentDictionary<string, IDisposable> _taskExecutionLimitDictionary;
    readonly WebServerCounter _webServerCounter;

    public TaskExecutionReportConsumerService(
        ApplicationRepositoryFactory<TaskExecutionInstanceModel> taskExecutionInstanceRepositoryFactory,
        ApplicationRepositoryFactory<TaskFlowExecutionInstanceModel> taskFlowExecutionInstanceRepoFactory,
        ApplicationRepositoryFactory<TaskFlowTemplateModel> taskFlowTemplateRepoFactory,
        ApplicationRepositoryFactory<TaskActivationRecordModel> taskActivationRecordRepoFactory,
        BatchQueue<TaskExecutionReportMessage> taskExecutionReportBatchQueue,
        [FromKeyedServices(nameof(TaskLogKafkaProducerService))]BatchQueue<TaskLogUnit> taskLogUnitBatchQueue,
        BatchQueue<TaskCancellationParameters> taskCancellationBatchQueue,
        BatchQueue<TaskActivateServiceParameters> taskScheduleQueue,
        ILogger<TaskExecutionReportConsumerService> logger,
        ConfigurationQueryService configurationQueryService,
        JobScheduler taskScheduler,
        IMemoryCache memoryCache,
        WebServerCounter webServerCounter,
        ExceptionCounter exceptionCounter,
        TaskFlowExecutor taskFlowExecutor,
        ITaskPenddingContextManager taskPenddingContextManager
    )
    {
        _logger = logger;
        _taskExecutionInstanceRepoFactory = taskExecutionInstanceRepositoryFactory;
        _taskActivationRecordRepoFactory = taskActivationRecordRepoFactory;
        _taskFlowExecutionInstanceRepoFactory = taskFlowExecutionInstanceRepoFactory;
        _taskFlowTemplateRepoFactory = taskFlowTemplateRepoFactory;
        _taskExecutionReportBatchQueue = taskExecutionReportBatchQueue;
        _taskActivateQueue = taskScheduleQueue;
        _taskCancellationBatchQueue = taskCancellationBatchQueue;
        _taskExecutionLimitDictionary = new ConcurrentDictionary<string, IDisposable>();
        _jobScheduler = taskScheduler;
        _taskLogUnitBatchQueue = taskLogUnitBatchQueue;
        _memoryCache = memoryCache;
        _webServerCounter = webServerCounter;
        _exceptionCounter = exceptionCounter;
        _taskFlowExecutor = taskFlowExecutor;
        _configurationQueryService = configurationQueryService;
        _taskPenddingContextManager = taskPenddingContextManager;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(
            ProcessExpiredTasksAsync(cancellationToken),
            ProcessTaskExecutionReportAsync(cancellationToken));
    }

    private async Task ProcessExpiredTasksAsync(CancellationToken cancellationToken = default)
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
            var stopwatch = new Stopwatch();
            _webServerCounter.TaskExecutionReportQueueCount.Value = _taskExecutionReportBatchQueue.QueueCount;
            if (array == null)
            {
                continue;
            }
            try
            {

                stopwatch.Start();
                var reports = array.Select(x => x.GetMessage()).ToArray();
                await ProcessTaskExecutionReportsAsync(reports, cancellationToken);
                stopwatch.Stop();

            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
            finally
            {
                _logger.LogInformation($"process {array.Length} messages,spent: {stopwatch.Elapsed}, QueueCount:{_taskExecutionReportBatchQueue.QueueCount}");
                _webServerCounter.TaskExecutionReportConsumeCount.Value += (uint)array.Length;
                _webServerCounter.TaskExecutionReportTotalTimeSpan.Value += stopwatch.Elapsed;
                stopwatch.Reset();
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


        var messageGroups = array.GroupBy(GetTaskId).ToArray();
        var taskExecutionInstanceIdList = Filter(messageGroups.Select(x => x.Key).Distinct()).ToArray();
        var taskExecutionInstanceIdFilters = DataFilterCollection<string>.Includes(taskExecutionInstanceIdList);
        var taskExecutionInstanceList = await GetTaskExeuctionInstanceListAsync(taskExecutionInstanceIdFilters, cancellationToken);

        if (taskExecutionInstanceList == null)
        {
            return;
        }
        var fireInstanceIdList = Filter(taskExecutionInstanceList.GroupBy(static x => x.FireInstanceId)
                                                                 .Select(static x => x.Key)
                                                                 .Distinct()).ToArray();

        var taskActivationRecordIdFilters = DataFilterCollection<string>.Includes(fireInstanceIdList);

        var taskActivationRecordList = await QueryActivationRecordAsync(taskActivationRecordIdFilters, cancellationToken);

        foreach (var taskExecutionInstanceGroup in taskExecutionInstanceList.GroupBy(static x => x.FireInstanceId))
        {
            var fireInstanceId = taskExecutionInstanceGroup.Key;
            if (fireInstanceId == null)
            {
                continue;
            }
            TaskActivationRecordModel? taskActivationRecord = null;
            foreach (var item in taskActivationRecordList)
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

        await SaveTaskExecutionInstanceListAsync(taskExecutionInstanceList, cancellationToken);
        await SaveTaskActivationRecordAsync(taskActivationRecordList, cancellationToken);

        await ProcessTaskFlowActiveRecordListAsync(taskActivationRecordList, cancellationToken);
    }

    async ValueTask SaveTaskActivationRecordAsync(
        List<TaskActivationRecordModel> taskActivationRecordList,
        CancellationToken cancellationToken = default)
    {
        await using var taskActivationRecordRepo = await _taskActivationRecordRepoFactory.CreateRepositoryAsync(cancellationToken);
        await taskActivationRecordRepo.UpdateRangeAsync(taskActivationRecordList, cancellationToken);
        var taskActivationRecordUpdateCount = taskActivationRecordRepo.LastSaveChangesCount;
        _webServerCounter.TaskExecutionReportSaveTimeSpan.Value += taskActivationRecordRepo.LastOperationTimeSpan;
        _webServerCounter.TaskExecutionReportSaveChangesCount.Value += (uint)taskActivationRecordRepo.LastSaveChangesCount;
    }

    async ValueTask  SaveTaskExecutionInstanceListAsync(
        List<TaskExecutionInstanceModel> taskExecutionInstanceList,
        CancellationToken cancellationToken = default)
    {
        await using var taskExecutionInstanceRepo = await _taskExecutionInstanceRepoFactory.CreateRepositoryAsync(cancellationToken);
        await taskExecutionInstanceRepo.UpdateRangeAsync(taskExecutionInstanceList, cancellationToken);
        var taskExecutionInstanceUpdateCount = taskExecutionInstanceRepo.LastSaveChangesCount;
    }

    async ValueTask<List<TaskActivationRecordModel>> QueryActivationRecordAsync(
        DataFilterCollection<string> taskActivationRecordIdFilters,
        CancellationToken cancellationToken = default)
    {
        await using var taskActivationRecordRepo = await _taskActivationRecordRepoFactory.CreateRepositoryAsync(cancellationToken);
        var taskActivationRecordList = await taskActivationRecordRepo.ListAsync(
                                                        new ListSpecification<TaskActivationRecordModel>(taskActivationRecordIdFilters),
                                                        cancellationToken);
        _webServerCounter.TaskExecutionReportQueryTimeSpan.Value += taskActivationRecordRepo.LastOperationTimeSpan;
        return taskActivationRecordList;
    }

    async ValueTask<List<TaskExecutionInstanceModel>> GetTaskExeuctionInstanceListAsync(
        DataFilterCollection<string> taskExecutionInstanceIdFilters,
        CancellationToken cancellationToken = default)
    {
        await using var taskExecutionInstanceRepo = await _taskExecutionInstanceRepoFactory.CreateRepositoryAsync(cancellationToken);
        var list = await taskExecutionInstanceRepo.ListAsync(
                                                                    new TaskExecutionInstanceListSpecification(taskExecutionInstanceIdFilters),
                                                                    cancellationToken);
        _webServerCounter.TaskExecutionReportQueryTimeSpan.Value += taskExecutionInstanceRepo.LastOperationTimeSpan;
        return list;
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

    private static LogEntry Convert(TaskExecutionLogEntry taskExecutionLogEntry)
    {
        return new LogEntry
        {
            DateTimeUtc = taskExecutionLogEntry.DateTime.ToDateTime().ToUniversalTime(),
            Type = (int)taskExecutionLogEntry.Type,
            Value = taskExecutionLogEntry.Value
        };
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

            stopwatchCollectLogEntries.Restart();
            foreach (var report in reports)
            {
                if (report.LogEntries.Count > 0)
                {
                    _webServerCounter.TaskLogUnitRecieveCount.Value++;
                    var taskLogUnit = new TaskLogUnit
                    {
                        Id = taskId,
                        LogEntries = report.LogEntries.Select(Convert).ToArray()
                    };
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
            await using var taskActiveRecordRepo = await _taskActivationRecordRepoFactory.CreateRepositoryAsync();
            foreach (var taskExecutionInstanceGroup in taskExecutionInstances.GroupBy(static x => x.FireInstanceId))
            {
                var fireInstanceId = taskExecutionInstanceGroup.Key;
                if (fireInstanceId == null) continue;
                var taskActiveRecord = await taskActiveRecordRepo.GetByIdAsync(fireInstanceId, cancellationToken);
                if (taskActiveRecord == null) continue;
                var taskDefinition = taskActiveRecord.GetTaskDefinition();
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
            await using var taskActivateRecordRepo = await _taskActivationRecordRepoFactory.CreateRepositoryAsync();
            foreach (var taskExecutionInstanceGroup in taskExecutionInstances.GroupBy(static x => x.FireInstanceId))
            {
                var fireInstanceId = taskExecutionInstanceGroup.Key;
                if (fireInstanceId == null) continue;
                var taskActiveRecord = await taskActivateRecordRepo.GetByIdAsync(fireInstanceId, cancellationToken);
                if (taskActiveRecord == null) continue;
                var taskDefinition = taskActiveRecord.GetTaskDefinition();
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
            taskExecutionInstanceId,
            dueTime,
            ProcessExcutionTimeLimitAsync);
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

    async Task<IDisposable> ProcessExcutionTimeLimitAsync(
        System.Reactive.Concurrency.IScheduler scheduler,
        string taskExecutionInstanceId,
        CancellationToken cancellationToken)
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
                    await RetryTaskAsync(
                        taskActivationRecord,
                        taskDefinitionSnapshot,
                        taskExecutionInstance,
                        cancellationToken);
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
                fireInstanceId,
                nodeList,
                taskActivationRecord.GetTaskDefinition()?.EnvironmentVariables ?? [],
                taskExecutionInstance.GetTaskFlowTaskKey());
            if (taskDefinitionSnapshot.RetryDuration == 0)
            {
                await _taskActivateQueue.SendAsync(new TaskActivateServiceParameters(fireTaskParameters), cancellationToken);
            }
            else
            {
                var dueTime = TimeSpan.FromSeconds(taskDefinitionSnapshot.RetryDuration);
                IDisposable[] tokens = [Disposable.Empty];
                tokens[0] = Scheduler.Default.ScheduleAsync(
                    dueTime,
                     async (scheduler, cancellationToken) =>
                     {
                         try
                         {
                             await _taskActivateQueue.SendAsync(new TaskActivateServiceParameters(fireTaskParameters), cancellationToken);
                             tokens[0].Dispose();
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
            await _taskActivateQueue.SendAsync(new TaskActivateServiceParameters(new FireTaskParameters
            {
                FireTimeUtc = DateTime.UtcNow,
                TriggerSource = TriggerSource.Manual,
                FireInstanceId = $"ChildTask_{Guid.NewGuid()}",
                TaskDefinitionId = childTaskDefinition.Id,
                ScheduledFireTimeUtc = DateTime.UtcNow,
                NodeList = taskDefinition.NodeList,
                ParentTaskExecutionInstanceId = parentTaskInstance.Id
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

        await using var taskActivationRecordRepo = await _taskActivationRecordRepoFactory.CreateRepositoryAsync();
        var taskActivationRecord = await taskActivationRecordRepo.GetByIdAsync(parentTaskInstance.FireInstanceId, cancellationToken);

        if (taskActivationRecord == null)
        {
            _logger.LogError($"Could not found task fire config:{parentTaskInstance.FireInstanceId}");
            return;
        }



        var taskDefinition = JsonSerializer.Deserialize<TaskDefinition>(taskActivationRecord.TaskDefinitionJson);
        if (taskDefinition == null) return;

        foreach (var childTaskDefinitionEntry in taskDefinition.ChildTaskDefinitions)
        {
            var taskDefinitionId = childTaskDefinitionEntry.Value;
            if (taskDefinitionId == null)
            {
                continue;
            }
            var childTaskDefinition = await FindTaskDefinitionAsync(taskDefinitionId, cancellationToken);
            if (childTaskDefinition == null)
            {
                continue;
            }
            var spec = new TaskExecutionInstanceListSpecification(
                    DataFilterCollection<TaskExecutionStatus>.Includes(
                    [
                        TaskExecutionStatus.Triggered,
                        TaskExecutionStatus.Running
                    ]),
                    DataFilterCollection<string>.Includes([parentTaskInstance.Id]),
                    DataFilterCollection<string>.Includes([childTaskDefinitionEntry.Id])
                    );

            await foreach (var instance in QueryTaskExecutionInstanceListAsync(spec, cancellationToken))
            {
                await _taskCancellationBatchQueue.SendAsync(new TaskCancellationParameters(
                    instance.Id,
                    nameof(CancelChildTasksAsync),
                    Dns.GetHostName()), cancellationToken);
                if (instance.ChildTaskScheduleCount == 0)
                {
                    continue;
                }
                _ = Task.Run(async () =>
                 {
                     await CancelChildTasksAsync(instance, cancellationToken);
                 });
            }


        }
    }

    async IAsyncEnumerable<TaskExecutionInstanceModel> QueryTaskExecutionInstanceListAsync(
        TaskExecutionInstanceListSpecification spec,
        [EnumeratorCancellation]CancellationToken cancellationToken = default)
    {
        await using var taskExecutionInstanceRepo = await _taskExecutionInstanceRepoFactory.CreateRepositoryAsync(cancellationToken);
        var instances = taskExecutionInstanceRepo.AsAsyncEnumerable(spec);
        await foreach (var instance in instances)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
            yield return instance;
        }
        yield break;
    }

    async ValueTask<TaskDefinitionModel?> FindTaskDefinitionAsync(
        string taskDefinitionId,
        CancellationToken cancellationToken)
    {
        var result = await _configurationQueryService.QueryConfigurationByIdListAsync<TaskDefinitionModel>(
            [taskDefinitionId],
            cancellationToken);
        return result.Items?.FirstOrDefault();
    }

    async ValueTask ProcessExpiredTaskExecutionInstanceAsync(CancellationToken cancellationToken = default)
    {
        try
        {

            var pageIndex = 1;
            var pageSize = 100;
            while (true)
            {


                var listQueryResult = await QueryTaskExecutionInstanceListAsync(
                    pageIndex,
                    pageSize,
                    cancellationToken);

                var taskExeuctionInstances = listQueryResult.Items;

                if (!listQueryResult.HasValue)
                {
                    break;
                }

                await ProcessTaskExecutionInstanceAsync(taskExeuctionInstances, cancellationToken);
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

    async ValueTask<ListQueryResult<TaskExecutionInstanceModel>> QueryTaskExecutionInstanceListAsync(
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        await using var taskExecutionInstanceRepo = await _taskExecutionInstanceRepoFactory.CreateRepositoryAsync();
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
        return listQueryResult;
    }

    async ValueTask ProcessTaskExecutionInstanceAsync(
        IEnumerable<TaskExecutionInstanceModel> taskExecutionInstances,
        CancellationToken cancellationToken = default)
    {
        List<TaskExecutionInstanceModel> taskExecutionInstanceList = [];
        foreach (var taskExecutionInstanceGroup in taskExecutionInstances.GroupBy(static x => x.FireInstanceId))
        {
            var taskFireInstanceId = taskExecutionInstanceGroup.Key;
            if (taskFireInstanceId == null) continue;
            var taskActiveRecord = await QueryTaskActiveRecordAsync(taskFireInstanceId, cancellationToken);
            if (taskActiveRecord == null) continue;
            taskExecutionInstanceList.Clear();
            var taskDefinition = taskActiveRecord.GetTaskDefinition();
            if (taskDefinition == null) continue;
            foreach (var taskExecutionInstance in taskExecutionInstanceGroup)
            {
                if (taskExecutionInstance.FireTimeUtc == DateTime.MinValue)
                {
                    continue;
                }

                if (DateTime.UtcNow - taskExecutionInstance.FireTimeUtc < TimeSpan.FromDays(7))
                {
                    continue;
                }
                else
                {
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

            }

            if (taskExecutionInstanceList.Count > 0)
            {
                await ScheduleTimeLimitTaskAsync(taskExecutionInstanceList, cancellationToken);
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
}