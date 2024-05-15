using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Models;

namespace NodeService.WebServer.Services.Tasks;

public class TaskScheduleService : BackgroundService
{
    private readonly ExceptionCounter _exceptionCounter;
    private readonly ILogger<TaskScheduleService> _logger;
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly ApplicationRepositoryFactory<JobScheduleConfigModel> _taskDefinitionRepositoryFactory;
    private readonly TaskSchedulerDictionary _taskSchedulerDictionary;

    private readonly IAsyncQueue<TaskScheduleMessage> _taskSchedulerMessageQueue;
    private IScheduler Scheduler;

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

    private async ValueTask ScheduleTaskAsync(
        TaskScheduleMessage taskScheduleMessage,
        CancellationToken cancellationToken = default)
    {
        if (taskScheduleMessage.TaskDefinitionId == null) return;
        using var repository = _taskDefinitionRepositoryFactory.CreateRepository();
        var taskDefinition = await repository.GetByIdAsync(taskScheduleMessage.TaskDefinitionId, cancellationToken);
        if (taskDefinition == null)
        {
            await CancelTaskAsync(taskScheduleMessage.TaskDefinitionId);
            return;
        }

        var taskSchedulerKey = new TaskSchedulerKey(
            taskScheduleMessage.TaskDefinitionId,
            taskScheduleMessage.TriggerSource);
        if (_taskSchedulerDictionary.TryGetValue(taskSchedulerKey, out var asyncDisposable))
        {
            await asyncDisposable.DisposeAsync();
            if (!taskDefinition.IsEnabled || taskScheduleMessage.IsCancellationRequested)
            {
                _taskSchedulerDictionary.TryRemove(taskSchedulerKey, out _);
                return;
            }

            var newAsyncDisposable = await ScheduleTaskAsync(
                taskSchedulerKey,
                taskScheduleMessage.ParentTaskExecutionInstanceId,
                taskDefinition);
            _taskSchedulerDictionary.TryUpdate(taskSchedulerKey, newAsyncDisposable, asyncDisposable);
        }
        else if (!taskScheduleMessage.IsCancellationRequested)
        {
            asyncDisposable = await ScheduleTaskAsync(
                taskSchedulerKey,
                taskScheduleMessage.ParentTaskExecutionInstanceId,
                taskDefinition);
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

    private async ValueTask ScheduleTasksAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var repository = _taskDefinitionRepositoryFactory.CreateRepository();
            var taskDefinitions =
                await repository.ListAsync(new TaskDefinitionSpecification(true, TaskTriggerType.Schedule));
            taskDefinitions = taskDefinitions.Where(x => x.IsEnabled && x.TriggerType == TaskTriggerType.Schedule)
                .ToList();
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

    private async ValueTask<IAsyncDisposable> ScheduleTaskAsync(
        TaskSchedulerKey taskSchedulerKey,
        string? parentTaskId,
        JobScheduleConfigModel taskDefinition,
        CancellationToken cancellationToken = default)
    {
        var taskScheduler = _serviceProvider.GetService<TaskScheduler>();
        if (taskScheduler == null) throw new InvalidOperationException();
        var asyncDisposable = await taskScheduler.ScheduleAsync<FireTaskJob>(taskSchedulerKey,
            taskSchedulerKey.TriggerSource == TaskTriggerSource.Schedule
                ? TriggerBuilderHelper.BuildScheduleTrigger(taskDefinition.CronExpressions.Select(x => x.Value))
                : TriggerBuilderHelper.BuildStartNowTrigger(),
            new Dictionary<string, object?>
            {
                {
                    nameof(ModelBase.Id),
                    taskDefinition.Id
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