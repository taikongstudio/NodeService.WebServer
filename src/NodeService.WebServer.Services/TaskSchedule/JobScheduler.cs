using Microsoft.Extensions.DependencyInjection;

namespace NodeService.WebServer.Services.TaskSchedule;

public class JobScheduler
{
    private readonly ExceptionCounter _exceptionCounter;
    private readonly TaskSchedulerDictionary _taskSchedulerDictionary;
    private readonly ILogger _logger;


    private readonly ISchedulerFactory _schedulerFactory;
    private readonly IServiceProvider _serviceProvider;
    private IScheduler _scheduler;

    public JobScheduler(
        ISchedulerFactory schedulerFactory,
        ILogger<JobScheduler> logger,
        [FromKeyedServices(nameof(TaskScheduleService))] TaskSchedulerDictionary taskSchedulerDictionary,
        IServiceProvider serviceProvider,
        ExceptionCounter exceptionCounter
    )
    {
        _schedulerFactory = schedulerFactory;
        _logger = logger;
        _taskSchedulerDictionary = taskSchedulerDictionary;
        _serviceProvider = serviceProvider;
        _exceptionCounter = exceptionCounter;
    }

    public async Task<IAsyncDisposable> ScheduleAsync<T>(
        TaskSchedulerKey taskSchedulerKey,
        IReadOnlyCollection<ITrigger> triggers,
        IDictionary<string, object?> properties,
        CancellationToken cancellationToken = default
    )
        where T : JobBase
    {
        _scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
        var asyncDisposable = new AsyncDisposable(_scheduler);
        try
        {
            var jobType = typeof(T);
            IDictionary<string, object?> props = new Dictionary<string, object?>
            {
                { nameof(JobBase.Properties), properties },
                { nameof(JobBase.TriggerSource), taskSchedulerKey.TriggerSource },
                {
                    nameof(JobBase.AsyncDispoable), taskSchedulerKey.TriggerSource
                                                    == TriggerSource.Manual
                        ? asyncDisposable
                        : null
                }
            };

            var job = JobBuilder.Create(jobType)
                .SetJobData(new JobDataMap(props))
                .WithIdentity(taskSchedulerKey.Key)
                .Build();
            asyncDisposable.SetKey(job.Key);
            Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> jobsAndTriggers = [];
            jobsAndTriggers.Add(job, triggers);
            await _scheduler.ScheduleJobs(jobsAndTriggers, true, cancellationToken);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }

        return asyncDisposable;
    }

    private class AsyncDisposable : IAsyncDisposable
    {
        private readonly IScheduler _scheduler;
        private Quartz.JobKey _jobKey;

        public AsyncDisposable(IScheduler scheduler)
        {
            _scheduler = scheduler;
        }

        public async ValueTask DisposeAsync()
        {
            if (_jobKey != null) await _scheduler.DeleteJob(_jobKey);
        }

        public void SetKey(Quartz.JobKey jobKey)
        {
            _jobKey = jobKey;
        }
    }
}