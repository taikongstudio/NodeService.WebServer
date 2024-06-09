﻿using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Logging;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.NodeSessions;
using System.Collections.Immutable;
using System.Linq.Expressions;

namespace NodeService.WebServer.Services.Tasks;

public class TaskExecutionReportConsumerService : BackgroundService
{
    private readonly ExceptionCounter _exceptionCounter;
    private readonly ILogger<TaskExecutionReportConsumerService> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly ApplicationRepositoryFactory<JobScheduleConfigModel> _taskDefinitionRepositoryFactory;
    private readonly ApplicationRepositoryFactory<JobExecutionInstanceModel> _taskExecutionInstanceRepositoryFactory;
    private readonly BatchQueue<JobExecutionReportMessage> _taskExecutionReportBatchQueue;
    private readonly ApplicationRepositoryFactory<JobFireConfigurationModel> _taskFireConfigRepositoryFactory;
    private readonly BatchQueue<TaskLogGroup> _taskLogGroupBatchQueue;
    private readonly IAsyncQueue<TaskScheduleMessage> _taskScheduleAsyncQueue;
    private readonly TaskScheduler _taskScheduler;
    private readonly TaskSchedulerDictionary _taskSchedulerDictionary;
    private readonly WebServerCounter _webServerCounter;

    public TaskExecutionReportConsumerService(
        ApplicationRepositoryFactory<JobExecutionInstanceModel> taskExecutionInstanceRepositoryFactory,
        ApplicationRepositoryFactory<JobScheduleConfigModel> taskDefinitionRepositoryFactory,
        ApplicationRepositoryFactory<JobFireConfigurationModel> taskFireConfigRepositoryFactory,
        BatchQueue<JobExecutionReportMessage> jobExecutionReportBatchQueue,
        BatchQueue<TaskLogGroup> taskLogGroupBatchQueue,
        IAsyncQueue<TaskScheduleMessage> jobScheduleAsyncQueue,
        ILogger<TaskExecutionReportConsumerService> logger,
        TaskSchedulerDictionary jobSchedulerDictionary,
        TaskScheduler jobScheduler,
        IMemoryCache memoryCache,
        WebServerCounter webServerCounter,
        ExceptionCounter exceptionCounter
    )
    {
        _taskExecutionInstanceRepositoryFactory = taskExecutionInstanceRepositoryFactory;
        _taskDefinitionRepositoryFactory = taskDefinitionRepositoryFactory;
        _taskFireConfigRepositoryFactory = taskFireConfigRepositoryFactory;
        _taskExecutionReportBatchQueue = jobExecutionReportBatchQueue;
        _taskScheduleAsyncQueue = jobScheduleAsyncQueue;
        _logger = logger;
        _taskSchedulerDictionary = jobSchedulerDictionary;
        _taskScheduler = jobScheduler;
        _taskLogGroupBatchQueue = taskLogGroupBatchQueue;
        _memoryCache = memoryCache;
        _webServerCounter = webServerCounter;
        _exceptionCounter = exceptionCounter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await InitAsync(stoppingToken);

        await foreach (var arrayPoolCollection in _taskExecutionReportBatchQueue.ReceiveAllAsync(stoppingToken))
        {

            var stopwatch = new Stopwatch();

            try
            {
                stopwatch.Start();
                await ProcessTaskExecutionReportsAsync(arrayPoolCollection, stoppingToken);
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

    async ValueTask InitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var taskExecutionInstanceRepo = _taskExecutionInstanceRepositoryFactory.CreateRepository();
            using var taskDefinitionRepo = _taskDefinitionRepositoryFactory.CreateRepository();
            var taskExeuctionInstances = await taskExecutionInstanceRepo.ListAsync(new TaskExecutionInstanceSpecification(
                DataFilterCollection<JobExecutionStatus>.Includes([JobExecutionStatus.Triggered, JobExecutionStatus.Started, JobExecutionStatus.Running]),
                DataFilterCollection<string>.Empty,
                DataFilterCollection<string>.Empty,
                DataFilterCollection<string>.Empty),
                cancellationToken);
            if (taskExeuctionInstances.Count == 0)
            {
                return;
            }
            List<JobExecutionInstanceModel> taskExecutionInstanceList = [];
            foreach (var taskExecutionInstanceGroup in taskExeuctionInstances.GroupBy(static x => x.JobScheduleConfigId))
            {
                var taskDefinitionId = taskExecutionInstanceGroup.Key;
                if (taskDefinitionId == null)
                {
                    continue;
                }
                var taskDefinition = await taskDefinitionRepo.GetByIdAsync(taskDefinitionId, cancellationToken);
                if (taskDefinition == null)
                {
                    continue;
                }
                taskExecutionInstanceList.Clear();
                foreach (var taskExecutionInstance in taskExecutionInstanceGroup)
                {
                    if (taskExecutionInstance.FireTimeUtc + TimeSpan.FromSeconds(taskDefinition.ExecutionLimitTimeSeconds) < DateTime.UtcNow)
                    {
                        await _taskExecutionReportBatchQueue.SendAsync(
                            new JobExecutionReportMessage()
                            {
                                NodeSessionId = new NodeSessionId(taskExecutionInstance.NodeInfoId),
                                Message = new JobExecutionReport()
                                {
                                    Status = JobExecutionStatus.Cancelled,
                                    Id = taskExecutionInstance.Id,
                                    Message = "Cancelled",
                                }
                            },
                            cancellationToken);
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
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }

    }

    private static string? GetTaskId(JobExecutionReportMessage? message)
    {
        if (message == null) return null;
        var report = message.GetMessage();
        if (report == null) return null;
        if (report.Properties.TryGetValue("Id", out var id)) return id;
        return report.Id;
    }

    private static LogEntry Convert(JobExecutionLogEntry taskExecutionLogEntry)
    {
        return new LogEntry
        {
            DateTimeUtc = taskExecutionLogEntry.DateTime.ToDateTime().ToUniversalTime(),
            Type = (int)taskExecutionLogEntry.Type,
            Value = taskExecutionLogEntry.Value
        };
    }

    private async Task ProcessTaskExecutionReportsAsync(
        ArrayPoolCollection<JobExecutionReportMessage?> arrayPoolCollection,
        CancellationToken stoppingToken = default)
    {
        var stopwatchSaveTimeSpan = TimeSpan.Zero;

        try
        {
            using var taskExecutionInstanceRepo = _taskExecutionInstanceRepositoryFactory.CreateRepository();
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
                    $"{nameof(TaskExecutionReportConsumerService)}:{nameof(JobExecutionInstanceModel)}:{taskId}";
                if (!_memoryCache.TryGetValue<JobExecutionInstanceModel>(cacheKey, out var taskExecutionInstance)
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
                        var taskLogGroup = new TaskLogGroup
                        {
                            Id = taskId,
                            LogEntries = report.LogEntries.Select(Convert).ToImmutableArray()
                        };
                        await _taskLogGroupBatchQueue.SendAsync(taskLogGroup);
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
                            stoppingToken);
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
                    var changesCount = await taskExecutionInstanceRepo.DbContext.Set<JobExecutionInstanceModel>()
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
                            stoppingToken);

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
        IEnumerable<JobExecutionInstanceModel> taskExecutionInstances,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (taskExecutionInstances == null || !taskExecutionInstances.Any()) return;
            using var taskDefinitionRepo = _taskDefinitionRepositoryFactory.CreateRepository();
            foreach (var taskExecutionInstanceGroup in taskExecutionInstances.GroupBy(static x => x.JobScheduleConfigId))
            {
                var taskDefinitionId = taskExecutionInstanceGroup.Key;
                if (taskDefinitionId == null)
                {
                    continue;
                }
                var taskDefinition = await taskDefinitionRepo.GetByIdAsync(taskDefinitionId, cancellationToken);
                if (taskDefinition == null)
                {
                    continue;
                }
                foreach (var taskExecutionInstance in taskExecutionInstanceGroup)
                {
                    if (taskDefinition == null || taskDefinition.ExecutionLimitTimeSeconds <= 0) continue;

                    var key = new TaskSchedulerKey(
                        taskExecutionInstance.Id,
                        TaskTriggerSource.Manual,
                        nameof(TaskExecutionTimeLimitJob));
                    await _taskScheduler.ScheduleAsync<TaskExecutionTimeLimitJob>(key,
                        TriggerBuilderHelper.BuildDelayTrigger(TimeSpan.FromSeconds(taskDefinition.ExecutionLimitTimeSeconds)),
                        new Dictionary<string, object?>
                        {
                            {
                                "TaskExecutionInstance",
                                taskExecutionInstance.JsonClone<JobExecutionInstanceModel>()
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
        JobExecutionInstanceModel taskExecutionInstance,
        NodeSessionMessage<JobExecutionReport> message,
        CancellationToken stoppingToken = default)
    {
        try
        {
            var report = message.GetMessage();


            var taskSchedulerKey = new TaskSchedulerKey(
                taskExecutionInstance.Id,
                TaskTriggerSource.Schedule,
                nameof(TaskExecutionTimeLimitJob)
                );
            switch (report.Status)
            {
                case JobExecutionStatus.Unknown:
                    break;
                case JobExecutionStatus.Triggered:
                    break;
                case JobExecutionStatus.Pendding:
                    break;
                case JobExecutionStatus.Started:
                    if (taskExecutionInstance.Status != JobExecutionStatus.Started)
                        await ScheduleTimeLimitTaskAsync([taskExecutionInstance], stoppingToken);
                    break;
                case JobExecutionStatus.Running:
                    break;
                case JobExecutionStatus.Failed:
                    await CancelTimeLimitTaskAsync(taskSchedulerKey);
                    break;
                case JobExecutionStatus.Finished:
                    if (taskExecutionInstance.Status != JobExecutionStatus.Finished)
                    {
                        await ScheduleChildTasksAsync(taskExecutionInstance, stoppingToken);
                        await CancelTimeLimitTaskAsync(taskSchedulerKey);
                    }

                    break;
                case JobExecutionStatus.Cancelled:
                    await CancelTimeLimitTaskAsync(taskSchedulerKey);
                    break;
                case JobExecutionStatus.PenddingTimeout:
                    break;
            }


            switch (report.Status)
            {
                case JobExecutionStatus.Unknown:
                    break;
                case JobExecutionStatus.Triggered:
                case JobExecutionStatus.Pendding:
                    break;
                case JobExecutionStatus.Started:
                    taskExecutionInstance.ExecutionBeginTimeUtc = DateTime.UtcNow;
                    break;
                case JobExecutionStatus.Running:
                    break;
                case JobExecutionStatus.Failed:
                case JobExecutionStatus.Finished:
                case JobExecutionStatus.Cancelled:
                    taskExecutionInstance.ExecutionEndTimeUtc = DateTime.UtcNow;
                    break;
            }

            if (taskExecutionInstance.Status < report.Status && report.Status != JobExecutionStatus.PenddingTimeout)
                taskExecutionInstance.Status = report.Status;
            if (report.Status == JobExecutionStatus.PenddingTimeout) taskExecutionInstance.Status = report.Status;
            if (!string.IsNullOrEmpty(report.Message)) taskExecutionInstance.Message = report.Message;
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }


    private async Task ScheduleChildTasksAsync(
        JobExecutionInstanceModel parentTaskInstance,
        CancellationToken stoppingToken = default)
    {
        var key = $"{nameof(JobFireConfigurationModel)}:{parentTaskInstance.FireInstanceId}";
        if (!_memoryCache.TryGetValue<JobFireConfigurationModel>(key, out var taskFireConfig))
        {
            using var taskFireConfigRepo = _taskFireConfigRepositoryFactory.CreateRepository();
            taskFireConfig = await taskFireConfigRepo.GetByIdAsync(parentTaskInstance.FireInstanceId, stoppingToken);
            _memoryCache.Set(key, taskFireConfig, TimeSpan.FromMinutes(30));
        }

        if (taskFireConfig == null)
        {
            _logger.LogError($"Could not found task fire config:{parentTaskInstance.FireInstanceId}");
            return;
        }

        using var taskDefinitionRepo = _taskDefinitionRepositoryFactory.CreateRepository();
        var taskScheduleConfig =
            JsonSerializer.Deserialize<JobScheduleConfiguration>(taskFireConfig.JobScheduleConfigJsonString);
        foreach (var childTaskDefinition in taskScheduleConfig.ChildTaskDefinitions)
        {
            var childTaskScheduleDefinition =
                await taskDefinitionRepo.GetByIdAsync(childTaskDefinition.Value, stoppingToken);
            if (childTaskScheduleDefinition == null) continue;
            await _taskScheduleAsyncQueue.EnqueueAsync(new TaskScheduleMessage(TaskTriggerSource.Parent,
                childTaskScheduleDefinition.Id, parentTaskInstanceId: parentTaskInstance.Id), stoppingToken);
        }
    }
}