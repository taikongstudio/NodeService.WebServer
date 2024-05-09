using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Logging;
using NodeService.WebServer.Data;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.NodeSessions;

namespace NodeService.WebServer.Services.Tasks;

public class TaskExecutionReportConsumerService : BackgroundService
{
    readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    readonly ILogger<TaskExecutionReportConsumerService> _logger;
    readonly BatchQueue<JobExecutionReportMessage> _taskExecutionReportBatchQueue;
    readonly TaskLogCacheManager _taskLogCacheManager;
    readonly IMemoryCache _memoryCache;
    readonly WebServerCounter _webServerCounter;
    readonly ExceptionCounter _exceptionCounter;
    readonly IAsyncQueue<TaskScheduleMessage> _taskScheduleAsyncQueue;
    readonly TaskScheduler _taskScheduler;
    readonly TaskSchedulerDictionary _taskSchedulerDictionary;

    public TaskExecutionReportConsumerService(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        BatchQueue<JobExecutionReportMessage> jobExecutionReportBatchQueue,
        TaskLogCacheManager taskLogCacheManager,
        IAsyncQueue<TaskScheduleMessage> jobScheduleAsyncQueue,
        ILogger<TaskExecutionReportConsumerService> logger,
        TaskSchedulerDictionary jobSchedulerDictionary,
        TaskScheduler jobScheduler,
        IMemoryCache memoryCache,
        WebServerCounter webServerCounter,
        ExceptionCounter exceptionCounter
    )
    {
        _dbContextFactory = dbContextFactory;
        _taskExecutionReportBatchQueue = jobExecutionReportBatchQueue;
        _taskScheduleAsyncQueue = jobScheduleAsyncQueue;
        _logger = logger;
        _taskSchedulerDictionary = jobSchedulerDictionary;
        _taskScheduler = jobScheduler;
        _taskLogCacheManager = taskLogCacheManager;
        _memoryCache = memoryCache;
        _webServerCounter = webServerCounter;
        _exceptionCounter = exceptionCounter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _taskLogCacheManager.Load();
        await foreach (var arrayPoolCollection in _taskExecutionReportBatchQueue.ReceiveAllAsync(stoppingToken))
        {
            var count = arrayPoolCollection.CountNotNull();
            if (count == 0) continue;

            var stopwatch = new Stopwatch();

            try
            {
                stopwatch.Start();
                await ProcessTaskExecutionReportsAsync(arrayPoolCollection, stoppingToken);
                stopwatch.Stop();
                _logger.LogInformation(
                    $"process {count} messages,spent: {stopwatch.Elapsed}, AvailableCount:{_taskExecutionReportBatchQueue.AvailableCount}");
                _webServerCounter.TaskExecutionReportAvailableCount = (uint)_taskExecutionReportBatchQueue.AvailableCount;
                _webServerCounter.TaskExecutionReportTotalTimeSpan += stopwatch.Elapsed;
                _webServerCounter.TaskExecutionReportConsumeCount += (uint)count;
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
                arrayPoolCollection.Dispose();
            }
        }
    }

    static string? GetTaskId(JobExecutionReportMessage? message)
    {
        if (message == null) return null;
        var report = message.GetMessage();
        if (report == null) return null;
        if (report.Properties.TryGetValue("Id", out var id)) return id;
        return report.Id;
    }

    static LogEntry Convert(JobExecutionLogEntry jobExecutionLogEntry)
    {
        return new LogEntry
        {
            DateTimeUtc = jobExecutionLogEntry.DateTime.ToDateTime().ToUniversalTime(),
            Type = (int)jobExecutionLogEntry.Type,
            Value = jobExecutionLogEntry.Value
        };
    }

    async Task ProcessTaskExecutionReportsAsync(
        ArrayPoolCollection<JobExecutionReportMessage?> arrayPoolCollection,
        CancellationToken stoppingToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var stopwatchSaveTimeSpan = TimeSpan.Zero;

        try
        {
            var stopwatchSave = new Stopwatch();
            var stopwatchQuery = new Stopwatch();
            var stopwatchProcessLogEntries = new Stopwatch();
            var stopwatchProcessMessage = new Stopwatch();
            List<TaskLogCachePage> taskLogPersistenceGroups = [];
            foreach (var taskReportGroup in arrayPoolCollection
                         .GroupBy(GetTaskId))
            {
                if (taskReportGroup.Key == null) continue;
                stopwatchQuery.Restart();
                var taskId = taskReportGroup.Key;
                var cacheKey = $"{nameof(TaskExecutionReportConsumerService)}:{nameof(JobExecutionInstanceModel)}:{taskId}";
                if (!_memoryCache.TryGetValue<JobExecutionInstanceModel>(cacheKey, out var taskExecutionInstance)
                    ||
                    taskExecutionInstance == null)
                {
                    taskExecutionInstance = await dbContext.FindAsync<JobExecutionInstanceModel>(taskId);
                    if (taskExecutionInstance == null)
                    {
                        return;
                    }
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
                        var taskLogCache = _taskLogCacheManager.GetCache(taskId);
                        var count1 = taskLogCache.Count;
                        _taskLogCacheManager.GetCache(taskId).AppendEntries(report.LogEntries.Select(Convert));
                        var count2 = taskLogCache.Count;
                        _webServerCounter.TaskExecutionReportProcessLogEntriesCount += (uint)(count2 - count1);
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
                            dbContext,
                            taskExecutionInstance,
                            reportMessage,
                            stoppingToken);
                    }
                    stopwatchProcessMessage.Stop();
                    _logger.LogInformation(
                        $"process {status} {messageStatusGroup.Count()} messages,spent:{stopwatchProcessMessage.Elapsed}");
                    _webServerCounter.TaskExecutionReportProcessTimeSpan += stopwatchProcessMessage.Elapsed;
                }

                stopwatchSave.Restart();
                int changesCount = await dbContext.JobExecutionInstancesDbSet.ExecuteUpdateAsync(
                    setPropertyCalls =>
                    setPropertyCalls.SetProperty(
                        task => task.Status,
                        task => task.Status != taskExecutionStatus ? task.Status : taskExecutionStatus)
                    .SetProperty(
                        task => task.Message,
                        task => task.Message != messsage ? task.Message : messsage)
                    .SetProperty(
                        task => task.ExecutionBeginTimeUtc,
                        task => task.ExecutionBeginTimeUtc != executionBeginTime ? task.ExecutionBeginTimeUtc : executionBeginTime)
                    .SetProperty(
                        task => task.ExecutionEndTimeUtc,
                        task => task.ExecutionEndTimeUtc != executionEndTime ? task.ExecutionEndTimeUtc : executionEndTime),
                    stoppingToken);

                stopwatchSave.Stop();
                stopwatchSaveTimeSpan += stopwatchSave.Elapsed;
                _webServerCounter.TaskExecutionReportSaveTimeSpan += stopwatchSave.Elapsed;
                _webServerCounter.TaskExecutionReportSaveChangesCount += (uint)changesCount;
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
                $"Process {arrayPoolCollection.CountNotNull()} messages, SaveElapsed:{stopwatchSaveTimeSpan}");
        }
    }

    async ValueTask CancelTimeLimitTaskAsync(TaskSchedulerKey jobSchedulerKey)
    {
        if (!_taskSchedulerDictionary.TryRemove(jobSchedulerKey, out var asyncDisposable)) return;
        if (asyncDisposable != null) await asyncDisposable.DisposeAsync();
    }

    async Task ScheduleTimeLimitTaskAsync(
        ApplicationDbContext dbContext,
        JobExecutionInstanceModel taskExecutionInstance,
        CancellationToken cancellationToken = default)
    {
        if (taskExecutionInstance == null) return;
        var cacheKey = $"{nameof(JobScheduleConfigModel)}:{taskExecutionInstance.JobScheduleConfigId}";
        if (!_memoryCache.TryGetValue<JobScheduleConfigModel>(cacheKey, out var taskScheduleConfig))
        {
            taskScheduleConfig = await dbContext.JobScheduleConfigurationDbSet.FirstOrDefaultAsync(
                        x => x.Id == taskExecutionInstance.JobScheduleConfigId,
                        cancellationToken);
            _memoryCache.Set(cacheKey, taskScheduleConfig, TimeSpan.FromMinutes(30));
        }
        if (taskScheduleConfig == null) return;
        if (taskScheduleConfig.ExecutionLimitTimeSeconds <= 0) return;
        var key = new TaskSchedulerKey(
            taskExecutionInstance.Id,
            TaskTriggerSource.Manual);
        await _taskScheduler.ScheduleAsync<TaskExecutionTimeLimitJob>(key,
            TriggerBuilderHelper.BuildDelayTrigger(TimeSpan.FromSeconds(taskScheduleConfig.ExecutionLimitTimeSeconds)),
            new Dictionary<string, object?>
            {
                {
                    "TaskExecutionInstance",
                    taskExecutionInstance.JsonClone<JobExecutionInstanceModel>()
                }
            });
    }

    async Task ProcessTaskExecutionReportAsync(
        ApplicationDbContext dbContext,
        JobExecutionInstanceModel taskExecutionInstance,
        NodeSessionMessage<JobExecutionReport> message,
        CancellationToken stoppingToken = default)
    {
        try
        {
            var report = message.GetMessage();


            var taskSchedulerKey = new TaskSchedulerKey(taskExecutionInstance.Id, TaskTriggerSource.Schedule);
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
                        await ScheduleTimeLimitTaskAsync(dbContext, taskExecutionInstance, stoppingToken);
                    break;
                case JobExecutionStatus.Running:
                    break;
                case JobExecutionStatus.Failed:
                    await CancelTimeLimitTaskAsync(taskSchedulerKey);
                    break;
                case JobExecutionStatus.Finished:
                    if (taskExecutionInstance.Status != JobExecutionStatus.Finished)
                    {
                        await ScheduleChildTasksAsync(dbContext, taskExecutionInstance, stoppingToken);
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


    async Task ScheduleChildTasksAsync(
        ApplicationDbContext dbContext,
        JobExecutionInstanceModel parentTaskInstance,
        CancellationToken stoppingToken = default)
    {
        var key = $"{nameof(JobFireConfigurationModel)}:{parentTaskInstance.FireInstanceId}";
        if (!_memoryCache.TryGetValue<JobFireConfigurationModel>(key, out var taskFireConfig))
        {
            taskFireConfig = await dbContext
            .JobFireConfigurationsDbSet
            .FirstOrDefaultAsync(x => x.Id == parentTaskInstance.FireInstanceId, stoppingToken);
            _memoryCache.Set(key, taskFireConfig, TimeSpan.FromMinutes(30));
        }

        if (taskFireConfig == null)
        {
            _logger.LogError($"Could not found task fire config:{parentTaskInstance.FireInstanceId}");
            return;
        }

        var taskScheduleConfig =
            JsonSerializer.Deserialize<JobScheduleConfiguration>(taskFireConfig.JobScheduleConfigJsonString);
        foreach (var childTaskDefinition in taskScheduleConfig.ChildTaskDefinitions)
        {
            var childTaskScheduleDefinition =
                await dbContext.JobScheduleConfigurationDbSet.FindAsync(childTaskDefinition.Value);
            if (childTaskScheduleDefinition == null) continue;
            await _taskScheduleAsyncQueue.EnqueueAsync(new TaskScheduleMessage(TaskTriggerSource.Parent,
                childTaskScheduleDefinition.Id, parentTaskInstanceId: parentTaskInstance.Id));
        }
    }
}