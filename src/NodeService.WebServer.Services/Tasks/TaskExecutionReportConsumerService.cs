using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Logging;
using NodeService.Infrastructure.Models;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.NodeSessions;
using System.Collections.Immutable;
using System.Net;

namespace NodeService.WebServer.Services.Tasks;

public class TaskExecutionReportConsumerService : BackgroundService
{
    readonly ExceptionCounter _exceptionCounter;
    readonly ILogger<TaskExecutionReportConsumerService> _logger;
    readonly IMemoryCache _memoryCache;
    readonly ApplicationRepositoryFactory<TaskDefinitionModel> _taskDefinitionRepoFactory;
    readonly ApplicationRepositoryFactory<TaskExecutionInstanceModel> _taskExecutionInstanceRepoFactory;
    readonly BatchQueue<TaskExecutionReportMessage> _taskExecutionReportBatchQueue;
    readonly ApplicationRepositoryFactory<TaskActivationRecordModel> _taskActivationRecordRepositoryFactory;
    readonly BatchQueue<TaskLogUnit> _taskLogUnitBatchQueue;
    readonly IAsyncQueue<TaskScheduleMessage> _taskScheduleAsyncQueue;
    readonly BatchQueue<TaskCancellationParameters> _taskCancellationBatchQueue;
    readonly TaskScheduler _taskScheduler;
    readonly TaskSchedulerDictionary _taskSchedulerDictionary;
    readonly WebServerCounter _webServerCounter;

    public TaskExecutionReportConsumerService(
        ApplicationRepositoryFactory<TaskExecutionInstanceModel> taskExecutionInstanceRepositoryFactory,
        ApplicationRepositoryFactory<TaskDefinitionModel> taskDefinitionRepositoryFactory,
        ApplicationRepositoryFactory<TaskActivationRecordModel> taskActivationRecordRepositoryFactory,
        BatchQueue<TaskExecutionReportMessage> jobExecutionReportBatchQueue,
        BatchQueue<TaskLogUnit> taskLogUnitBatchQueue,
        BatchQueue<TaskCancellationParameters> taskCancellationBatchQueue,
        IAsyncQueue<TaskScheduleMessage> taskScheduleAsyncQueue,
        ILogger<TaskExecutionReportConsumerService> logger,
        TaskSchedulerDictionary taskSchedulerDictionary,
        TaskScheduler jobScheduler,
        IMemoryCache memoryCache,
        WebServerCounter webServerCounter,
        ExceptionCounter exceptionCounter
    )
    {
        _taskExecutionInstanceRepoFactory = taskExecutionInstanceRepositoryFactory;
        _taskDefinitionRepoFactory = taskDefinitionRepositoryFactory;
        _taskActivationRecordRepositoryFactory = taskActivationRecordRepositoryFactory;
        _taskExecutionReportBatchQueue = jobExecutionReportBatchQueue;
        _taskScheduleAsyncQueue = taskScheduleAsyncQueue;
        _taskCancellationBatchQueue = taskCancellationBatchQueue;
        _logger = logger;
        _taskSchedulerDictionary = taskSchedulerDictionary;
        _taskScheduler = jobScheduler;
        _taskLogUnitBatchQueue = taskLogUnitBatchQueue;
        _memoryCache = memoryCache;
        _webServerCounter = webServerCounter;
        _exceptionCounter = exceptionCounter;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _ = Task.Factory.StartNew(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await ScanExpiredTaskExecutionInstanceAsync(cancellationToken);
                await Task.Delay(TimeSpan.FromHours(6), cancellationToken);
            }
        });

        await foreach (var array in _taskExecutionReportBatchQueue.ReceiveAllAsync(cancellationToken))
        {
            if (array == null)
            {
                continue;
            }
            var stopwatch = new Stopwatch();

            try
            {
                stopwatch.Start();
                await ProcessTaskExecutionReportsAsync(array, cancellationToken);
                stopwatch.Stop();
                _logger.LogInformation(
                    $"process {array.Length} messages,spent: {stopwatch.Elapsed}, AvailableCount:{_taskExecutionReportBatchQueue.AvailableCount}");
                _webServerCounter.TaskExecutionReportAvailableCount =
                    (uint)_taskExecutionReportBatchQueue.AvailableCount;
                _webServerCounter.TaskExecutionReportTotalTimeSpan += stopwatch.Elapsed;
                _webServerCounter.TaskExecutionReportConsumeCount += (uint)array.Length;
                stopwatch.Reset();
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
            finally
            {
                stopwatch.Reset();
            }
        }
    }


    private static string? GetTaskId(TaskExecutionReportMessage? message)
    {
        if (message == null) return null;
        var report = message.GetMessage();
        if (report == null) return null;
        if (report.Properties.TryGetValue("Id", out var id)) return id;
        return report.Id;
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

    private async Task ProcessTaskExecutionReportsAsync(
        TaskExecutionReportMessage[] array,
        CancellationToken cancellationToken = default)
    {
        var stopwatchSaveTimeSpan = TimeSpan.Zero;

        try
        {
            using var taskExecutionInstanceRepo = _taskExecutionInstanceRepoFactory.CreateRepository();
            var stopwatchSave = new Stopwatch();
            var stopwatchQuery = new Stopwatch();
            var stopwatchProcessLogEntries = new Stopwatch();
            var stopwatchProcessMessage = new Stopwatch();
            foreach (var taskReportGroup in array.GroupBy(GetTaskId))
            {
                if (taskReportGroup.Key == null) continue;
                stopwatchQuery.Restart();
                var taskId = taskReportGroup.Key;
                var taskExecutionInstance = await taskExecutionInstanceRepo.GetByIdAsync(taskId);
                stopwatchQuery.Stop();
                _logger.LogInformation($"{taskId}:QueryElapsed:{stopwatchQuery.Elapsed}");
                _webServerCounter.TaskExecutionReportQueryTimeSpan += stopwatchQuery.Elapsed;


                if (taskExecutionInstance == null || IsTerminatedStatus(taskExecutionInstance)) continue;
                

                stopwatchProcessLogEntries.Restart();
                foreach (var reportMessage in taskReportGroup)
                {
                    if (reportMessage == null) continue;
                    var report = reportMessage.GetMessage();
                    if (report.LogEntries.Count > 0)
                    {
                        var taskLogUnit = new TaskLogUnit
                        {
                            Id = taskId,
                            LogEntries = report.LogEntries.Select(Convert).ToImmutableArray()
                        };
                        await _taskLogUnitBatchQueue.SendAsync(taskLogUnit, cancellationToken);
                        _logger.LogInformation($"Send task log unit:{taskId}");
                    }
                }

                stopwatchProcessLogEntries.Stop();
                _webServerCounter.TaskExecutionReportProcessLogEntriesTimeSpan += stopwatchProcessLogEntries.Elapsed;


                var taskExecutionStatus = taskExecutionInstance.Status;
                var messsage = taskExecutionInstance.Message;
                var executionBeginTime = taskExecutionInstance.ExecutionBeginTimeUtc;
                var executionEndTime = taskExecutionInstance.ExecutionEndTimeUtc;
                foreach (var messageStatusGroup in taskReportGroup.GroupBy(static x => x.GetMessage().Status).OrderBy(static x => x.Key))
                {
                    stopwatchProcessMessage.Restart();
                    var status = messageStatusGroup.Key;
                    foreach (var reportMessage in messageStatusGroup)
                    {
                        if (reportMessage == null) continue;
                        await ProcessTaskExecutionReportAsync(
                            taskExecutionInstance,
                            reportMessage,
                            cancellationToken);
                    }

                    stopwatchProcessMessage.Stop();
                    _logger.LogInformation(
                        $"process {status} {messageStatusGroup.Count()} messages,spent:{stopwatchProcessMessage.Elapsed}");
                    _webServerCounter.TaskExecutionReportProcessTimeSpan += stopwatchProcessMessage.Elapsed;
                }

                var diffCount = 0;
                if (taskExecutionStatus != taskExecutionInstance.Status)
                {
                    taskExecutionStatus = taskExecutionInstance.Status;
                    diffCount++;
                }

                if (messsage != taskExecutionInstance.Message)
                {
                    messsage = taskExecutionInstance.Message;
                    diffCount++;
                }

                if (executionBeginTime != taskExecutionInstance.ExecutionBeginTimeUtc)
                {
                    executionBeginTime = taskExecutionInstance.ExecutionBeginTimeUtc;
                    diffCount++;
                }

                if (executionEndTime != taskExecutionInstance.ExecutionEndTimeUtc)
                {
                    executionEndTime = taskExecutionInstance.ExecutionEndTimeUtc;
                    diffCount++;
                }

                if (diffCount > 0)
                {
                    stopwatchSave.Restart();
                    var changesCount = await taskExecutionInstanceRepo.DbContext.Set<TaskExecutionInstanceModel>()
                        .Where(x => x.Id == taskId)
                        .ExecuteUpdateAsync(
                            setPropertyCalls =>
                                setPropertyCalls.SetProperty(
                                        task => task.Status,
                                        taskExecutionStatus)
                                    .SetProperty(
                                        task => task.Message,
                                        messsage)
                                    .SetProperty(
                                        task => task.ExecutionBeginTimeUtc,
                                        executionBeginTime)
                                    .SetProperty(
                                        task => task.ExecutionEndTimeUtc,
                                        executionEndTime),
                            cancellationToken);

                    stopwatchSave.Stop();
                    stopwatchSaveTimeSpan += stopwatchSave.Elapsed;
                    _webServerCounter.TaskExecutionReportSaveTimeSpan += stopwatchSave.Elapsed;
                    _webServerCounter.TaskExecutionReportSaveChangesCount += (uint)changesCount;
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
            _logger.LogInformation($"Process {array.Length} messages, SaveElapsed:{stopwatchSaveTimeSpan}");
        }
    }

    bool IsTerminatedStatus(TaskExecutionInstanceModel taskExecutionInstance)
    {
        switch (taskExecutionInstance.Status)
        {
            case TaskExecutionStatus.Unknown:
                break;
            case TaskExecutionStatus.Triggered:
                break;
            case TaskExecutionStatus.Pendding:
                break;
            case TaskExecutionStatus.Started:
                break;
            case TaskExecutionStatus.Running:
                break;
            case TaskExecutionStatus.Failed:
            case TaskExecutionStatus.Finished:
            case TaskExecutionStatus.Cancelled:
            case TaskExecutionStatus.PenddingTimeout:
                return true;
            case TaskExecutionStatus.MaxCount:
                break;
            default:
                break;
        }
        return false;
    }

    private async ValueTask CancelTimeLimitTaskAsync(TaskSchedulerKey jobSchedulerKey)
    {
        if (!_taskSchedulerDictionary.TryRemove(jobSchedulerKey, out var asyncDisposable)) return;
        if (asyncDisposable != null) await asyncDisposable.DisposeAsync();
    }

    private async Task ScheduleTimeLimitTaskAsync(
        IEnumerable<TaskExecutionInstanceModel> taskExecutionInstances,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (taskExecutionInstances == null || !taskExecutionInstances.Any()) return;
            using var taskDefinitionRepo = _taskDefinitionRepoFactory.CreateRepository();
            foreach (var taskExecutionInstanceGroup in
                     taskExecutionInstances.GroupBy(static x => x.TaskDefinitionId))
            {
                var taskDefinitionId = taskExecutionInstanceGroup.Key;
                if (taskDefinitionId == null) continue;
                var taskDefinition = await taskDefinitionRepo.GetByIdAsync(taskDefinitionId, cancellationToken);
                if (taskDefinition == null) continue;
                foreach (var taskExecutionInstance in taskExecutionInstanceGroup)
                {
                    if (taskDefinition == null || taskDefinition.ExecutionLimitTimeSeconds <= 0) continue;

                    var key = new TaskSchedulerKey(
                        taskExecutionInstance.Id,
                        TaskTriggerSource.Manual,
                        nameof(TaskExecutionTimeLimitJob));
                    await _taskScheduler.ScheduleAsync<TaskExecutionTimeLimitJob>(key,
                        TriggerBuilderHelper.BuildDelayTrigger(
                            TimeSpan.FromSeconds(taskDefinition.ExecutionLimitTimeSeconds)),
                        new Dictionary<string, object?>
                        {
                            {
                                "TaskExecutionInstance",
                                taskExecutionInstance.JsonClone<TaskExecutionInstanceModel>()
                            }
                        }, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    private async Task ProcessTaskExecutionReportAsync(
        TaskExecutionInstanceModel taskExecutionInstance,
        NodeSessionMessage<TaskExecutionReport> message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var report = message.GetMessage();


            var taskExecutionTimeLimitJobKey = new TaskSchedulerKey(
                taskExecutionInstance.Id,
                TaskTriggerSource.Schedule,
                nameof(TaskExecutionTimeLimitJob)
            );
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
                        await ScheduleTimeLimitTaskAsync([taskExecutionInstance], cancellationToken);
                    break;
                case TaskExecutionStatus.Running:
                    break;
                case TaskExecutionStatus.Failed:
                    await CancelTimeLimitTaskAsync(taskExecutionTimeLimitJobKey);
                    break;
                case TaskExecutionStatus.Finished:
                    if (taskExecutionInstance.Status != TaskExecutionStatus.Finished)
                    {
                        await ScheduleChildTasksAsync(taskExecutionInstance, cancellationToken);
                        await CancelTimeLimitTaskAsync(taskExecutionTimeLimitJobKey);
                    }

                    break;
                case TaskExecutionStatus.Cancelled:
                    await CancelTimeLimitTaskAsync(taskExecutionTimeLimitJobKey);

                    break;
                case TaskExecutionStatus.PenddingTimeout:
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
                    if (taskExecutionInstance.ParentId == null)
                        await CancelChildTasksAsync(taskExecutionInstance, cancellationToken);
                    break;
            }

            if (taskExecutionInstance.Status < report.Status && report.Status != TaskExecutionStatus.PenddingTimeout)
                taskExecutionInstance.Status = report.Status;
            if (report.Status == TaskExecutionStatus.PenddingTimeout) taskExecutionInstance.Status = report.Status;
            if (!string.IsNullOrEmpty(report.Message))
            {
                var key = $"{nameof(TaskCancellationQueueService)}:{taskExecutionInstance.Id}";
                if (report.Status == TaskExecutionStatus.Cancelled
                    &&
                    _memoryCache.TryGetValue<TaskCancellationParameters>(key, out var parameters) && parameters != null)
                {
                    _memoryCache.Remove(key);
                    taskExecutionInstance.Message = $"{nameof(parameters.Source)}: {parameters.Source},{nameof(parameters.IpAddressOrHostName)}: {parameters.IpAddressOrHostName}";
                }
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }


    private async Task ScheduleChildTasksAsync(
        TaskExecutionInstanceModel parentTaskInstance,
        CancellationToken cancellationToken = default)
    {
        using var taskActivationRecordRepo = _taskActivationRecordRepositoryFactory.CreateRepository();
        var taskActivationRecord =
            await taskActivationRecordRepo.GetByIdAsync(parentTaskInstance.FireInstanceId, cancellationToken);

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
            var childTaskScheduleDefinition =
                await taskDefinitionRepo.GetByIdAsync(childTaskDefinition.Value, cancellationToken);
            if (childTaskScheduleDefinition == null) continue;
            await _taskScheduleAsyncQueue.EnqueueAsync(new TaskScheduleMessage(TaskTriggerSource.Parent,
                childTaskScheduleDefinition.Id, parentTaskInstanceId: parentTaskInstance.Id), cancellationToken);
        }
    }

    private async Task CancelChildTasksAsync(
        TaskExecutionInstanceModel parentTaskInstance,
        CancellationToken cancellationToken = default)
    {
        using var taskActivationRecordRepo = _taskActivationRecordRepositoryFactory.CreateRepository();
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
                new TaskExecutionInstanceSpecification(
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
                    nameof(CancelChildTasksAsync),
                    Dns.GetHostName(),
                    childTaskExectionInstance.Id), cancellationToken);
            }

        }
    }

    private async ValueTask ScanExpiredTaskExecutionInstanceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var taskExecutionInstanceRepo = _taskExecutionInstanceRepoFactory.CreateRepository();
            using var taskDefinitionRepo = _taskDefinitionRepoFactory.CreateRepository();
            var taskExeuctionInstances = await taskExecutionInstanceRepo.ListAsync(
                new TaskExecutionInstanceSpecification(
                    DataFilterCollection<TaskExecutionStatus>.Includes(
                    [
                        TaskExecutionStatus.Triggered,
                        TaskExecutionStatus.Started,
                        TaskExecutionStatus.Running
                    ]),
                    false), cancellationToken);

            if (taskExeuctionInstances.Count == 0) return;
            List<TaskExecutionInstanceModel> taskExecutionInstanceList = [];
            foreach (var taskExecutionInstanceGroup in
                     taskExeuctionInstances.GroupBy(static x => x.TaskDefinitionId))
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
                            nameof(TaskExecutionReportConsumerService),
                            Dns.GetHostName(),
                            taskExecutionInstance.Id), cancellationToken);
                    }
                    else
                    {
                        taskExecutionInstanceList.Add(taskExecutionInstance);
                    }
                }

                if (taskExecutionInstanceList.Count > 0)
                    await ScheduleTimeLimitTaskAsync(taskExecutionInstanceList, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }
}