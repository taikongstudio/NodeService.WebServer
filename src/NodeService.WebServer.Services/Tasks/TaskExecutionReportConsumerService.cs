﻿using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Logging;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.NodeSessions;
using System.Collections.Immutable;

namespace NodeService.WebServer.Services.Tasks;

public class TaskExecutionReportConsumerService : BackgroundService
{
    private readonly ExceptionCounter _exceptionCounter;
    private readonly ILogger<TaskExecutionReportConsumerService> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly ApplicationRepositoryFactory<TaskDefinitionModel> _taskDefinitionRepoFactory;
    private readonly ApplicationRepositoryFactory<TaskExecutionInstanceModel> _taskExecutionInstanceRepoFactory;
    private readonly BatchQueue<TaskExecutionReportMessage> _taskExecutionReportBatchQueue;
    private readonly ApplicationRepositoryFactory<TaskActivationRecordModel> _taskActivationRecordRepositoryFactory;
    private readonly BatchQueue<TaskLogUnit> _taskLogUnitBatchQueue;
    private readonly IAsyncQueue<TaskScheduleMessage> _taskScheduleAsyncQueue;
    private readonly BatchQueue<TaskCancellationParameters> _taskCancellationBatchQueue;
    private readonly TaskScheduler _taskScheduler;
    private readonly TaskSchedulerDictionary _taskSchedulerDictionary;
    private readonly WebServerCounter _webServerCounter;

    public TaskExecutionReportConsumerService(
        ApplicationRepositoryFactory<TaskExecutionInstanceModel> taskExecutionInstanceRepositoryFactory,
        ApplicationRepositoryFactory<TaskDefinitionModel> taskDefinitionRepositoryFactory,
        ApplicationRepositoryFactory<TaskActivationRecordModel> taskActivationRecordRepositoryFactory,
        BatchQueue<TaskExecutionReportMessage> jobExecutionReportBatchQueue,
        BatchQueue<TaskLogUnit> taskLogUnitBatchQueue,
        BatchQueue<TaskCancellationParameters> taskCancellationBatchQueue,
        IAsyncQueue<TaskScheduleMessage> jobScheduleAsyncQueue,
        ILogger<TaskExecutionReportConsumerService> logger,
        TaskSchedulerDictionary jobSchedulerDictionary,
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
        _taskScheduleAsyncQueue = jobScheduleAsyncQueue;
        _taskCancellationBatchQueue = taskCancellationBatchQueue;
        _logger = logger;
        _taskSchedulerDictionary = jobSchedulerDictionary;
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

        await foreach (var arrayPoolCollection in _taskExecutionReportBatchQueue.ReceiveAllAsync(cancellationToken))
        {
            var stopwatch = new Stopwatch();

            try
            {
                stopwatch.Start();
                await ProcessTaskExecutionReportsAsync(arrayPoolCollection, cancellationToken);
                stopwatch.Stop();
                _logger.LogInformation(
                    $"process {arrayPoolCollection.Count} messages,spent: {stopwatch.Elapsed}, AvailableCount:{_taskExecutionReportBatchQueue.AvailableCount}");
                _webServerCounter.TaskExecutionReportAvailableCount =
                    (uint)_taskExecutionReportBatchQueue.AvailableCount;
                _webServerCounter.TaskExecutionReportTotalTimeSpan += stopwatch.Elapsed;
                _webServerCounter.TaskExecutionReportConsumeCount += (uint)arrayPoolCollection.Count;
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

    private async ValueTask ScanExpiredTaskExecutionInstanceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var taskExecutionInstanceRepo = _taskExecutionInstanceRepoFactory.CreateRepository();
            using var taskDefinitionRepo = _taskDefinitionRepoFactory.CreateRepository();
            var taskExeuctionInstances = await taskExecutionInstanceRepo.ListAsync(
                new TaskExecutionInstanceSpecification(
                    DataFilterCollection<TaskExecutionStatus>.Includes([
                        TaskExecutionStatus.Triggered, TaskExecutionStatus.Started, TaskExecutionStatus.Running
                    ]),
                    DataFilterCollection<string>.Empty,
                    DataFilterCollection<string>.Empty,
                    DataFilterCollection<string>.Empty),
                cancellationToken);
            if (taskExeuctionInstances.Count == 0) return;
            List<TaskExecutionInstanceModel> taskExecutionInstanceList = [];
            foreach (var taskExecutionInstanceGroup in
                     taskExeuctionInstances.GroupBy(static x => x.JobScheduleConfigId))
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
                    if (taskExecutionInstance.FireTimeUtc +
                        TimeSpan.FromSeconds(taskDefinition.ExecutionLimitTimeSeconds) < DateTime.UtcNow)
                        await _taskExecutionReportBatchQueue.SendAsync(
                            new TaskExecutionReportMessage()
                            {
                                NodeSessionId = new NodeSessionId(taskExecutionInstance.NodeInfoId),
                                Message = new TaskExecutionReport()
                                {
                                    Status = TaskExecutionStatus.Cancelled,
                                    Id = taskExecutionInstance.Id,
                                    Message = "Cancelled"
                                }
                            },
                            cancellationToken);
                    else
                        taskExecutionInstanceList.Add(taskExecutionInstance);
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
        ArrayPoolCollection<TaskExecutionReportMessage?> arrayPoolCollection,
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
            foreach (var taskReportGroup in arrayPoolCollection
                         .GroupBy(GetTaskId))
            {
                if (taskReportGroup.Key == null) continue;
                stopwatchQuery.Restart();
                var taskId = taskReportGroup.Key;
                var cacheKey =
                    $"{nameof(TaskExecutionReportConsumerService)}:{nameof(TaskExecutionInstanceModel)}:{taskId}";
                if (!_memoryCache.TryGetValue<TaskExecutionInstanceModel>(cacheKey, out var taskExecutionInstance)
                    ||
                    taskExecutionInstance == null)
                {
                    taskExecutionInstance = await taskExecutionInstanceRepo.GetByIdAsync(taskId);
                    if (taskExecutionInstance == null) continue;
                    _memoryCache.Set(cacheKey, taskExecutionInstance, TimeSpan.FromHours(1));
                }

                stopwatchQuery.Stop();
                _logger.LogInformation($"{taskId}:QueryElapsed:{stopwatchQuery.Elapsed}");
                _webServerCounter.TaskExecutionReportQueryTimeSpan += stopwatchQuery.Elapsed;


                if (taskExecutionInstance == null) continue;


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
                        await _taskLogUnitBatchQueue.SendAsync(taskLogUnit);
                    }
                }

                stopwatchProcessLogEntries.Stop();
                _webServerCounter.TaskExecutionReportProcessLogEntriesTimeSpan += stopwatchProcessLogEntries.Elapsed;


                var taskExecutionStatus = taskExecutionInstance.Status;
                var messsage = taskExecutionInstance.Message;
                var executionBeginTime = taskExecutionInstance.ExecutionBeginTimeUtc;
                var executionEndTime = taskExecutionInstance.ExecutionEndTimeUtc;
                foreach (var messageStatusGroup in taskReportGroup.GroupBy(static x => x.GetMessage().Status))
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
            _logger.LogInformation(
                $"Process {arrayPoolCollection.Count} messages, SaveElapsed:{stopwatchSaveTimeSpan}");
        }
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
                     taskExecutionInstances.GroupBy(static x => x.JobScheduleConfigId))
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
            if (!string.IsNullOrEmpty(report.Message)) taskExecutionInstance.Message = report.Message;
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
            if (childTaskScheduleDefinition == null) continue;
            var childTaskExectionInstances = await taskExecutionInstanceRepo.ListAsync(
                new TaskExecutionInstanceSpecification(
                    DataFilterCollection<TaskExecutionStatus>.Includes(
                    [
                        TaskExecutionStatus.Triggered,
                        TaskExecutionStatus.Running
                    ]),
                    default,
                    DataFilterCollection<string>.Includes([childTaskDefinition.Id]),
                    default), cancellationToken);
            if (childTaskExectionInstances.Count == 0) continue;
            foreach (var childTaskExectionInstance in childTaskExectionInstances)
                await _taskCancellationBatchQueue.SendAsync(new TaskCancellationParameters()
                {
                    UserName = nameof(CancelChildTasksAsync),
                    TaskExeuctionInstanceId = childTaskExectionInstance.Id
                }, cancellationToken);
        }
    }
}