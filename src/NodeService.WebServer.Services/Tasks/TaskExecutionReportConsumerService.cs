using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Logging;
using NodeService.WebServer.Data;
using NodeService.WebServer.Services.NodeSessions;

namespace NodeService.WebServer.Services.Tasks;

public class TaskExecutionReportConsumerService : BackgroundService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ILogger<TaskExecutionReportConsumerService> _logger;
    private readonly BatchQueue<JobExecutionReportMessage> _taskExecutionReportBatchQueue;
    private readonly TaskLogCacheManager _taskLogCacheManager;
    private readonly IAsyncQueue<TaskScheduleMessage> _taskScheduleAsyncQueue;
    private readonly TaskScheduler _taskScheduler;
    private readonly TaskSchedulerDictionary _taskSchedulerDictionary;

    public TaskExecutionReportConsumerService(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        BatchQueue<JobExecutionReportMessage> jobExecutionReportBatchQueue,
        TaskLogCacheManager taskLogCacheManager,
        IAsyncQueue<TaskScheduleMessage> jobScheduleAsyncQueue,
        ILogger<TaskExecutionReportConsumerService> logger,
        TaskSchedulerDictionary jobSchedulerDictionary,
        TaskScheduler jobScheduler
    )
    {
        _dbContextFactory = dbContextFactory;
        _taskExecutionReportBatchQueue = jobExecutionReportBatchQueue;
        _taskScheduleAsyncQueue = jobScheduleAsyncQueue;
        _logger = logger;
        _taskSchedulerDictionary = jobSchedulerDictionary;
        _taskScheduler = jobScheduler;
        _taskLogCacheManager = taskLogCacheManager;
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
                stopwatch.Reset();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
            }
            finally
            {
                stopwatch.Reset();
                arrayPoolCollection.Dispose();
            }
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

    private static LogEntry Convert(JobExecutionLogEntry jobExecutionLogEntry)
    {
        return new LogEntry
        {
            DateTimeUtc = jobExecutionLogEntry.DateTime.ToDateTime().ToUniversalTime(),
            Type = (int)jobExecutionLogEntry.Type,
            Value = jobExecutionLogEntry.Value
        };
    }

    private async Task ProcessTaskExecutionReportsAsync(
        ArrayPoolCollection<JobExecutionReportMessage?> arrayPoolCollection,
        CancellationToken stoppingToken = default)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        var stopwatchSave = new Stopwatch();
        var timeSpan = TimeSpan.Zero;
        try
        {
            List<TaskLogCachePage> taskLogPersistenceGroups = [];
            foreach (var taskReportGroup in arrayPoolCollection
                         .GroupBy(GetTaskId))
            {
                if (taskReportGroup.Key == null) continue;
                var stopwatchQuery = Stopwatch.StartNew();
                var taskId = taskReportGroup.Key;
                var taskInstance = await dbContext.FindAsync<JobExecutionInstanceModel>(taskId);
                if (taskInstance == null) continue;
                await dbContext.Entry(taskInstance).ReloadAsync();
                stopwatchQuery.Stop();
                _logger.LogInformation($"{taskId}:Query:{stopwatchQuery.Elapsed}");

                if (taskInstance == null) continue;


                foreach (var reportMessage in taskReportGroup)
                {
                    if (reportMessage == null) continue;
                    var report = reportMessage.GetMessage();
                    if (report.LogEntries.Count > 0)
                        _taskLogCacheManager.GetCache(taskId).AppendEntries(report.LogEntries.Select(Convert));
                }


                var stopwatchProcessMessage = new Stopwatch();
                foreach (var messageStatusGroup in taskReportGroup.GroupBy(static x => x.GetMessage().Status))
                {
                    stopwatchProcessMessage.Start();

                    foreach (var reportMessage in messageStatusGroup)
                    {
                        if (reportMessage == null) continue;
                        await ProcessTaskExecutionReportAsync(taskInstance, reportMessage, stoppingToken);
                    }

                    stopwatchProcessMessage.Stop();
                    _logger.LogInformation(
                        $"process:{messageStatusGroup.Count()},spent:{stopwatchProcessMessage.Elapsed}");
                    stopwatchProcessMessage.Reset();
                    stopwatchSave.Start();
                    await dbContext.SaveChangesAsync(stoppingToken);
                    stopwatchSave.Stop();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
        }
        finally
        {
            _logger.LogInformation(
                $"Process {arrayPoolCollection.CountNotNull()} messages, SaveElapsed:{stopwatchSave.Elapsed}");
        }
    }

    private async ValueTask CancelTimeLimitTaskAsync(TaskSchedulerKey jobSchedulerKey)
    {
        if (!_taskSchedulerDictionary.TryRemove(jobSchedulerKey, out var asyncDisposable)) return;
        if (asyncDisposable != null) await asyncDisposable.DisposeAsync();
    }

    private async Task ScheduleTimeLimitTaskAsync(
        JobExecutionInstanceModel taskExecutionInstance,
        CancellationToken cancellationToken = default)
    {
        if (taskExecutionInstance == null) return;
        using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var taskScheduleConfig = await dbContext.JobScheduleConfigurationDbSet.FindAsync(
            [taskExecutionInstance.JobScheduleConfigId],
            cancellationToken);
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

    private async Task ProcessTaskExecutionReportAsync(
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
                        await ScheduleTimeLimitTaskAsync(taskExecutionInstance, stoppingToken);
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
            _logger.LogError(ex.ToString());
        }
    }


    private async Task ScheduleChildTasksAsync(
        JobExecutionInstanceModel parentTaskInstance,
        CancellationToken stoppingToken = default)
    {
        using var dbContext = await _dbContextFactory.CreateDbContextAsync(stoppingToken);
        var taskFireConfig = await dbContext
            .JobFireConfigurationsDbSet
            .FindAsync([parentTaskInstance.FireInstanceId], stoppingToken);
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