using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.TaskSchedule;
using Quartz.Spi;

namespace NodeService.WebServer.Services.TaskSchedule;


public partial  class TaskScheduleService : BackgroundService
{
    readonly ExceptionCounter _exceptionCounter;
    private readonly WebServerCounter _webServerCounter;
    readonly ILogger<TaskScheduleService> _logger;
    readonly JobScheduler _jobScheduler;
    readonly IJobFactory _jobFactory;
    readonly ISchedulerFactory _schedulerFactory;
    readonly ApplicationRepositoryFactory<TaskDefinitionModel> _taskDefinitionRepositoryFactory;
    readonly ApplicationRepositoryFactory<TaskFlowTemplateModel> _taskFlowTemplateRepoFactory;
    readonly ApplicationRepositoryFactory<PropertyBag> _propertyBagRepoFactory;
    readonly TaskSchedulerDictionary _taskSchedulerDictionary;

    readonly IAsyncQueue<AsyncOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>> _taskScheduleServiceParametersQueue;
    IScheduler Scheduler;

    public TaskScheduleService(
        ILogger<TaskScheduleService> logger,
        IJobFactory jobFactory,
        IAsyncQueue<AsyncOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>> taskScheduleServiceParametersQueue,
        ApplicationRepositoryFactory<TaskDefinitionModel> taskDefinitionRepoFactory,
        ApplicationRepositoryFactory<TaskFlowTemplateModel> taskFlowTemplateRepoFactory,
        ApplicationRepositoryFactory<PropertyBag> propertyBagRepoFactory,
        JobScheduler jobScheduler,
        ISchedulerFactory schedulerFactory,
        [FromKeyedServices(nameof(TaskScheduleService))] TaskSchedulerDictionary taskSchedulerDictionary,
        ExceptionCounter exceptionCounter,
        WebServerCounter webServerCounter)
    {
        _jobFactory = jobFactory;
        _schedulerFactory = schedulerFactory;
        _taskDefinitionRepositoryFactory = taskDefinitionRepoFactory;
        _taskFlowTemplateRepoFactory = taskFlowTemplateRepoFactory;
        _propertyBagRepoFactory = propertyBagRepoFactory;
        _logger = logger;
        _jobScheduler = jobScheduler;
        _taskSchedulerDictionary = taskSchedulerDictionary;
        _taskScheduleServiceParametersQueue = taskScheduleServiceParametersQueue;
        _exceptionCounter = exceptionCounter;
        _webServerCounter = webServerCounter;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        Scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
        Scheduler.JobFactory = _jobFactory;
        if (!Scheduler.IsStarted) await Scheduler.Start(cancellationToken);
        bool schedule = !Debugger.IsAttached;
        if (schedule)
        {
            await ScheduleTaskDefinitionsAsync(cancellationToken);
            await ScheduleTaskFlowTemplatesAsync(cancellationToken);
            await ScheduleNodeHealthyCheckAsync(cancellationToken);
            await ScheduleTaskObservationAsync(cancellationToken);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var op = await _taskScheduleServiceParametersQueue.DeuqueAsync(cancellationToken);
            try
            {
                switch (op.Argument.Parameters.Index)
                {
                    case 0:
                        await ProcessTaskDefinitionScheduleParametersAsync(op, cancellationToken);
                        break;
                    case 1:
                        await ProcessTaskFlowScheduleParametersAsync(op, cancellationToken);
                        break;
                    case 2:
                        await ProcessNodeHealthyCheckScheduleParametersAsync(op, cancellationToken);
                        break;
                    case 3:
                        await ProcessTaskObservationScheduleParametersAsync(op, cancellationToken);
                        break;
                    default:
                        break;
                }
                op.TrySetResult(new TaskScheduleServiceResult());
            }
            catch (Exception ex)
            {
                op.TrySetException(ex);
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
        }
    }

    private async ValueTask DeleteAllTaskScheduleAsync(string key, string context)
    {
        for (var triggerSource = TriggerSource.Schedule; triggerSource < TriggerSource.Max - 1; triggerSource++)
        {
            var taskSchedulerKey = new TaskSchedulerKey(key, triggerSource, context);
            if (_taskSchedulerDictionary.TryRemove(taskSchedulerKey, out var asyncDisposable))
                await asyncDisposable.DisposeAsync();
        }
    }

    ITrigger TimeOnlyToTrigger(TimeOnlyModel timeOnlyModel)
    {
        return TriggerBuilder.Create()
            .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(timeOnlyModel.Time.Hour, timeOnlyModel.Time.Minute))
            .Build();
    }

    ITrigger IntervalToTrigger(int minutes)
    {
        return TriggerBuilder.Create()
            .WithDailyTimeIntervalSchedule(builder => builder.WithInterval(minutes, IntervalUnit.Minute))
            .Build();
    }
}