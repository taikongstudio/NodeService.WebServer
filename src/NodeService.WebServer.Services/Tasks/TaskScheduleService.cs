using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Concurrent;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Models;

namespace NodeService.WebServer.Services.Tasks;

public class TaskScheduleService : BackgroundService
{
    readonly ApplicationRepositoryFactory<JobScheduleConfigModel> _taskDefinitionRepositoryFactory;
    readonly ILogger<TaskScheduleService> _logger;
    readonly ISchedulerFactory _schedulerFactory;
    readonly IServiceProvider _serviceProvider;
    readonly TaskSchedulerDictionary _taskSchedulerDictionary;

    readonly IAsyncQueue<TaskScheduleMessage> _taskSchedulerMessageQueue;
    readonly ExceptionCounter _exceptionCounter;
    IScheduler Scheduler;

    public TaskScheduleService(
        IServiceProvider serviceProvider,
        IAsyncQueue<TaskScheduleMessage> taskScheduleMessageQueue,
        ApplicationRepositoryFactory<JobScheduleConfigModel> taskDefinitionRepositoryFactory,
        ISchedulerFactory schedulerFactory,
        TaskSchedulerDictionary taskSchedulerDictionary,
        ILogger<TaskScheduleService> logger,
        ExceptionCounter exceptionCounter)
    {
        _serviceProvider = serviceProvider;
        _taskDefinitionRepositoryFactory = taskDefinitionRepositoryFactory;
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
        await ScheduleTasksAsync(stoppingToken);
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

    async ValueTask ScheduleTaskAsync(
        TaskScheduleMessage taskScheduleMessage,
        CancellationToken cancellationToken = default)
    {
        if (taskScheduleMessage.TaskScheduleConfigId == null) return;
        using var repository = _taskDefinitionRepositoryFactory.CreateRepository();
        var taskScheduleConfig = await repository.GetByIdAsync(taskScheduleMessage.TaskScheduleConfigId, cancellationToken);
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

    async ValueTask CancelTaskAsync(string key)
    {
        for (var i = TaskTriggerSource.Schedule; i < TaskTriggerSource.Max - 1; i++)
        {
            var taskSchedulerKey = new TaskSchedulerKey(key, TaskTriggerSource.Schedule);
            if (_taskSchedulerDictionary.TryRemove(taskSchedulerKey, out var asyncDisposable))
                await asyncDisposable.DisposeAsync();
        }
    }

    async ValueTask ScheduleTasksAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var repository = _taskDefinitionRepositoryFactory.CreateRepository();
            var taskDefinitions = await repository.ListAsync(new TaskDefinitionSpecification(true, TaskTriggerType.Schedule));
            taskDefinitions = taskDefinitions.Where(x => x.IsEnabled && x.TriggerType == TaskTriggerType.Schedule).ToList();
            foreach (var taskDefinition in taskDefinitions)
                await _taskSchedulerMessageQueue.EnqueueAsync(
                    new TaskScheduleMessage(TaskTriggerSource.Schedule, taskDefinition.Id),
                    cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
        }
    }

    async ValueTask<IAsyncDisposable> ScheduleTaskAsync(
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