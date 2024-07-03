using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using Quartz.Spi;
using OneOf;

namespace NodeService.WebServer.Services.Tasks;

public readonly record struct TaskScheduleParameters
{
    public  string? ParentTaskExecutionInstanceId { get; init; }

    public  string TaskDefinitionId { get; init; }

    public  TriggerSource TriggerSource { get; init; }

    public TaskScheduleParameters(
        TriggerSource triggerSource,
        string taskDefinitionId,
        string? parentTaskInstanceId = null)
    {
        TriggerSource = triggerSource;
        TaskDefinitionId = taskDefinitionId;
        ParentTaskExecutionInstanceId = parentTaskInstanceId;
    }
}

public readonly record struct TaskFlowScheduleParameters
{
    public TaskFlowScheduleParameters(
        TriggerSource triggerSource,
        string taskFlowTemplateId,
        string parentTaskFlowInstanceId)
    {
        TriggerSource = triggerSource;
        TaskFlowTemplateId = taskFlowTemplateId;
        ParentTaskFlowInstanceId = parentTaskFlowInstanceId;
    }

    public string TaskFlowTemplateId { get; init; }

    public string ParentTaskFlowInstanceId { get; init; }

    public TriggerSource TriggerSource { get; init; }
}

public readonly record struct TaskScheduleServiceParameters
{
    public TaskScheduleServiceParameters(TaskScheduleParameters parameters)
    {
        Parameters = parameters;
    }

    public TaskScheduleServiceParameters(TaskFlowScheduleParameters parameters)
    {
        Parameters = parameters;
    }

    public OneOf<TaskScheduleParameters, TaskFlowScheduleParameters> Parameters { get; init; }
}

public readonly record struct TaskScheduleServiceResult
{

}


public class TaskScheduleService : BackgroundService
{
    private readonly ExceptionCounter _exceptionCounter;
    private readonly ILogger<TaskScheduleService> _logger;
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly ApplicationRepositoryFactory<TaskDefinitionModel> _taskDefinitionRepositoryFactory;
    private readonly TaskSchedulerDictionary _taskSchedulerDictionary;

    private readonly IAsyncQueue<BatchQueueOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>> _taskScheduleServiceParametersQueue;
    private IScheduler Scheduler;

    public TaskScheduleService(
        IServiceProvider serviceProvider,
        IAsyncQueue<BatchQueueOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>> taskScheduleServiceParametersQueue,
        ApplicationRepositoryFactory<TaskDefinitionModel> taskDefinitionRepositoryFactory,
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
        _taskScheduleServiceParametersQueue = taskScheduleServiceParametersQueue;
        _exceptionCounter = exceptionCounter;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        Scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
        Scheduler.JobFactory = _serviceProvider.GetService<IJobFactory>();
        if (!Scheduler.IsStarted) await Scheduler.Start(cancellationToken);
        await ScheduleTasksAsync(cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            var op = await _taskScheduleServiceParametersQueue.DeuqueAsync(cancellationToken);
            try
            {
                switch (op.Argument.Parameters.Index)
                {
                    case 0:
                        await ProcessTaskScheduleParametersAsync(op, cancellationToken);
                        break;
                    case 1:
                        break;
                    default:
                        break;
                }

            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
        }
    }

    async Task ProcessTaskScheduleParametersAsync(
        BatchQueueOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult> op,
        CancellationToken cancellationToken = default)
    {
        switch (op.Kind)
        {
            case BatchQueueOperationKind.None:
                break;
            case BatchQueueOperationKind.AddOrUpdate:
                await ScheduleTaskAsync(op.Argument.Parameters.AsT0, cancellationToken);
                break;
            case BatchQueueOperationKind.Delete:
                await DeleteAllTaskScheduleAsync(op.Argument.Parameters.AsT0.TaskDefinitionId);
                break;
            case BatchQueueOperationKind.Query:
                break;
            default:
                break;
        }
    }

    private async ValueTask ScheduleTaskAsync(
        TaskScheduleParameters taskScheduleParameters,
        CancellationToken cancellationToken = default)
    {
        if (taskScheduleParameters.TaskDefinitionId == null) return;
        using var repository = _taskDefinitionRepositoryFactory.CreateRepository();
        var taskDefinition = await repository.GetByIdAsync(taskScheduleParameters.TaskDefinitionId, cancellationToken);
        if (taskDefinition == null || taskDefinition.Value.TriggerType != TaskTriggerType.Schedule)
        {
            await DeleteAllTaskScheduleAsync(taskScheduleParameters.TaskDefinitionId);
            return;
        }



        var taskSchedulerKey = new TaskSchedulerKey(
            taskScheduleParameters.TaskDefinitionId,
            taskScheduleParameters.TriggerSource,
            nameof(FireTaskJob));

        if (_taskSchedulerDictionary.TryGetValue(taskSchedulerKey, out var asyncDisposable))
        {
            await asyncDisposable.DisposeAsync();
            if (!taskDefinition.IsEnabled)
            {
                await DeleteAllTaskScheduleAsync(taskScheduleParameters.TaskDefinitionId);
                return;
            }

            var newAsyncDisposable = await ScheduleTaskAsync(
                taskSchedulerKey,
                taskScheduleParameters.ParentTaskExecutionInstanceId,
                taskDefinition,
                cancellationToken);
            _taskSchedulerDictionary.TryUpdate(taskSchedulerKey, newAsyncDisposable, asyncDisposable);
        }
        else
        {
            asyncDisposable = await ScheduleTaskAsync(
                taskSchedulerKey,
                taskScheduleParameters.ParentTaskExecutionInstanceId,
                taskDefinition,
                cancellationToken);
            _taskSchedulerDictionary.TryAdd(taskSchedulerKey, asyncDisposable);
        }
    }

    private async ValueTask DeleteAllTaskScheduleAsync(string key)
    {
        for (var triggerSource = TriggerSource.Schedule; triggerSource < TriggerSource.Max - 1; triggerSource++)
        {
            var taskSchedulerKey = new TaskSchedulerKey(key, triggerSource, nameof(FireTaskJob));
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
            {
                var taskScheduleParameters = new TaskScheduleParameters(TriggerSource.Schedule, taskDefinition.Id);
                var taskScheduleServiceParameters = new TaskScheduleServiceParameters(taskScheduleParameters);
                var op = new BatchQueueOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>(
                    taskScheduleServiceParameters,
                    BatchQueueOperationKind.AddOrUpdate);
                await _taskScheduleServiceParametersQueue.EnqueueAsync(op, cancellationToken);
            }

        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    private async ValueTask<IAsyncDisposable> ScheduleTaskAsync(
        TaskSchedulerKey taskSchedulerKey,
        string? parentTaskId,
        TaskDefinitionModel taskDefinition,
        CancellationToken cancellationToken = default)
    {
        var taskScheduler = _serviceProvider.GetService<TaskScheduler>();
        var asyncDisposable = await taskScheduler.ScheduleAsync<FireTaskJob>(taskSchedulerKey,
            TriggerBuilderHelper.BuildScheduleTrigger(taskDefinition.CronExpressions.Select(x => x.Value)),
            new Dictionary<string, object?>
            {
                {
                    nameof(TaskDefinitionModel.Id),
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