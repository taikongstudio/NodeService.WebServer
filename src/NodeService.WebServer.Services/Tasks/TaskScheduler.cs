using Microsoft.Extensions.DependencyInjection;
using NodeService.WebServer.Models;

namespace NodeService.WebServer.Services.Tasks;

public class TaskScheduler
{
    private readonly ExceptionCounter _exceptionCounter;
    private readonly TaskSchedulerDictionary _jobSchedulerDictionary;
    private readonly ILogger _logger;


    private readonly ISchedulerFactory _schedulerFactory;
    private readonly IServiceProvider _serviceProvider;
    private IScheduler _scheduler;

    public TaskScheduler(
        ISchedulerFactory schedulerFactory,
        ILogger<TaskScheduler> logger,
        TaskSchedulerDictionary jobSchedulerDictionary,
        IServiceProvider serviceProvider,
        ExceptionCounter exceptionCounter
    )
    {
        _schedulerFactory = schedulerFactory;
        _logger = logger;
        _jobSchedulerDictionary = jobSchedulerDictionary;
        _serviceProvider = serviceProvider;
        _exceptionCounter = exceptionCounter;
    }

    public async Task<IAsyncDisposable> ScheduleAsync<T>(
        TaskSchedulerKey jobSchedulerKey,
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
                { nameof(JobBase.Logger), _serviceProvider.GetService<ILogger<T>>() },
                { nameof(JobBase.Properties), properties },
                { nameof(JobBase.ServiceProvider), _serviceProvider },
                { nameof(JobBase.TriggerSource), jobSchedulerKey.TriggerSource },
                {
                    nameof(JobBase.AsyncDispoable), jobSchedulerKey.TriggerSource
                                                    == TaskTriggerSource.Manual
                        ? asyncDisposable
                        : null
                }
            };

            var job = JobBuilder.Create(jobType)
                .SetJobData(new JobDataMap(props))
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
        private JobKey _jobKey;

        public AsyncDisposable(IScheduler scheduler)
        {
            _scheduler = scheduler;
        }

        public async ValueTask DisposeAsync()
        {
            if (_jobKey != null) await _scheduler.DeleteJob(_jobKey);
        }

        public void SetKey(JobKey jobKey)
        {
            _jobKey = jobKey;
        }
    }
}