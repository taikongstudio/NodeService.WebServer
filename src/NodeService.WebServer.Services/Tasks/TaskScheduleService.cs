using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Data;
using NodeService.WebServer.Models;

namespace NodeService.WebServer.Services.Tasks;

public class TaskScheduleService : BackgroundService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ILogger<TaskScheduleService> _logger;
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly TaskSchedulerDictionary _taskSchedulerDictionary;

    private readonly IAsyncQueue<TaskScheduleMessage> _taskSchedulerMessageQueue;
    private readonly ExceptionCounter _exceptionCounter;
    private IScheduler Scheduler;

    public TaskScheduleService(
        IServiceProvider serviceProvider,
        IAsyncQueue<TaskScheduleMessage> taskScheduleMessageQueue,
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        ISchedulerFactory schedulerFactory,
        TaskSchedulerDictionary taskSchedulerDictionary,
        ILogger<TaskScheduleService> logger,
        ExceptionCounter exceptionCounter)
    {
        _serviceProvider = serviceProvider;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
        _schedulerFactory = schedulerFactory;
        _taskSchedulerDictionary = taskSchedulerDictionary;
        _taskSchedulerMessageQueue = taskScheduleMessageQueue;
        _exceptionCounter = exceptionCounter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Scheduler = await _schedulerFactory.GetScheduler(stoppingToken);
        if (!Scheduler.IsStarted) await Scheduler.Start(stoppingToken);

        await ScheduleTasksFromDbContextAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            var taskScheduleMessage = await _taskSchedulerMessageQueue.DeuqueAsync(stoppingToken);
            try
            {
                await ScheduleTaskAsync(taskScheduleMessage, stoppingToken);
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
        }
    }

    private async ValueTask ScheduleTaskAsync(
        TaskScheduleMessage taskScheduleMessage,
        CancellationToken cancellationToken = default)
    {
        if (taskScheduleMessage.TaskScheduleConfigId == null) return;
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var taskScheduleConfig = await dbContext
            .JobScheduleConfigurationDbSet
            .AsQueryable()
            .FirstOrDefaultAsync(
                x => x.Id == taskScheduleMessage.TaskScheduleConfigId,
                cancellationToken);
        if (taskScheduleConfig == null)
        {
            await CancelTaskAsync(taskScheduleMessage.TaskScheduleConfigId);
            return;
        }

        var taskSchedulerKey = new TaskSchedulerKey(
            taskScheduleMessage.TaskScheduleConfigId,
            taskScheduleMessage.TriggerSource);
        if (_taskSchedulerDictionary.TryGetValue(taskSchedulerKey, out var asyncDisposable))
        {
            await asyncDisposable.DisposeAsync();
            if (!taskScheduleConfig.IsEnabled || taskScheduleMessage.IsCancellationRequested)
            {
                _taskSchedulerDictionary.TryRemove(taskSchedulerKey, out _);
                return;
            }

            var newAsyncDisposable = await ScheduleTaskAsync(
                taskSchedulerKey,
                taskScheduleMessage.ParentTaskExecutionInstanceId,
                taskScheduleConfig);
            _taskSchedulerDictionary.TryUpdate(taskSchedulerKey, newAsyncDisposable, asyncDisposable);
        }
        else if (!taskScheduleMessage.IsCancellationRequested)
        {
            asyncDisposable = await ScheduleTaskAsync(
                taskSchedulerKey,
                taskScheduleMessage.ParentTaskExecutionInstanceId,
                taskScheduleConfig);
            _taskSchedulerDictionary.TryAdd(taskSchedulerKey, asyncDisposable);
        }
    }

    private async ValueTask CancelTaskAsync(string key)
    {
        for (var i = TaskTriggerSource.Schedule; i < TaskTriggerSource.Max - 1; i++)
        {
            var taskSchedulerKey = new TaskSchedulerKey(key, TaskTriggerSource.Schedule);
            if (_taskSchedulerDictionary.TryRemove(taskSchedulerKey, out var asyncDisposable))
                await asyncDisposable.DisposeAsync();
        }
    }

    private async ValueTask ScheduleTasksFromDbContextAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var taskScheduleConfigs = await dbContext.JobScheduleConfigurationDbSet
                .AsAsyncEnumerable()
                .Where(x => x.IsEnabled && x.TriggerType == JobScheduleTriggerType.Schedule)
                .ToListAsync(cancellationToken);
            foreach (var jobScheduleConfig in taskScheduleConfigs)
                await _taskSchedulerMessageQueue.EnqueueAsync(
                    new TaskScheduleMessage(TaskTriggerSource.Schedule, jobScheduleConfig.Id),
                    cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
        }
    }

    private async ValueTask<IAsyncDisposable> ScheduleTaskAsync(
        TaskSchedulerKey taskSchedulerKey,
        string? parentTaskId,
        JobScheduleConfigModel taskScheduleConfig,
        CancellationToken cancellationToken = default)
    {
        var taskScheduler = _serviceProvider.GetService<TaskScheduler>();
        if (taskScheduler == null) throw new InvalidOperationException();
        var asyncDisposable = await taskScheduler.ScheduleAsync<FireTaskJob>(taskSchedulerKey,
            taskSchedulerKey.TriggerSource == TaskTriggerSource.Schedule
                ? TriggerBuilderHelper.BuildScheduleTrigger(taskScheduleConfig.CronExpressions.Select(x => x.Value))
                : TriggerBuilderHelper.BuildStartNowTrigger(),
            new Dictionary<string, object?>
            {
                {
                    nameof(FireTaskParameters.TaskScheduleConfig),
                    taskScheduleConfig
                },
                {
                    nameof(FireTaskParameters.ParentTaskId),
                    parentTaskId
                }
            },
            cancellationToken
        );
        return asyncDisposable;
    }
}