using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using Quartz.Spi;
using OneOf;
using NodeService.Infrastructure.DataModels;
using System.Threading;
using System;

namespace NodeService.WebServer.Services.Tasks;

public readonly record struct TaskScheduleParameters
{
    public  string TaskDefinitionId { get; init; }

    public  TriggerSource TriggerSource { get; init; }

    public TaskScheduleParameters(
        TriggerSource triggerSource,
        string taskDefinitionId)
    {
        TriggerSource = triggerSource;
        TaskDefinitionId = taskDefinitionId;
    }
}

public readonly record struct TaskFlowScheduleParameters
{
    public TaskFlowScheduleParameters(
        TriggerSource triggerSource,
        string taskFlowTemplateId)
    {
        TriggerSource = triggerSource;
        TaskFlowTemplateId = taskFlowTemplateId;
    }

    public string TaskFlowTemplateId { get; init; }

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


public partial  class TaskScheduleService : BackgroundService
{
    readonly ExceptionCounter _exceptionCounter;
    readonly ILogger<TaskScheduleService> _logger;
    readonly JobScheduler _jobScheduler;
    readonly IJobFactory _jobFactory;
    readonly ISchedulerFactory _schedulerFactory;
    readonly ApplicationRepositoryFactory<TaskDefinitionModel> _taskDefinitionRepositoryFactory;
    readonly ApplicationRepositoryFactory<TaskFlowTemplateModel> _taskFlowTemplateRepoFactory;
    readonly TaskSchedulerDictionary _taskSchedulerDictionary;

    readonly IAsyncQueue<BatchQueueOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>> _taskScheduleServiceParametersQueue;
    IScheduler Scheduler;

    public TaskScheduleService(
        ILogger<TaskScheduleService> logger,
        IJobFactory  jobFactory,
        IAsyncQueue<BatchQueueOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>> taskScheduleServiceParametersQueue,
        ApplicationRepositoryFactory<TaskDefinitionModel> taskDefinitionRepoFactory,
        ApplicationRepositoryFactory<TaskFlowTemplateModel> taskFlowTemplateRepoFactory,
        JobScheduler jobScheduler,
        ISchedulerFactory schedulerFactory,
        TaskSchedulerDictionary taskSchedulerDictionary,

        ExceptionCounter exceptionCounter)
    {
        _jobFactory = jobFactory;
        _schedulerFactory = schedulerFactory;
        _taskDefinitionRepositoryFactory = taskDefinitionRepoFactory;
        _taskFlowTemplateRepoFactory = taskFlowTemplateRepoFactory;
        _logger = logger;
        _jobScheduler = jobScheduler;
        _taskSchedulerDictionary = taskSchedulerDictionary;
        _taskScheduleServiceParametersQueue = taskScheduleServiceParametersQueue;
        _exceptionCounter = exceptionCounter;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        Scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
        Scheduler.JobFactory = _jobFactory;
        if (!Scheduler.IsStarted) await Scheduler.Start(cancellationToken);
        await ScheduleTasksAsync(cancellationToken);
        await ScheduleTaskFlowsAsync(cancellationToken);
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
                        await ProcessTaskFlowScheduleParametersAsync(op, cancellationToken);
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

}