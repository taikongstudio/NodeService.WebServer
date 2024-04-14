

using Microsoft.Extensions.DependencyInjection;
using System;
using System.Reactive.Disposables;

namespace NodeService.WebServer.Services.Tasks
{
    public class TaskScheduler
    {
        private class AsyncDisposable : IAsyncDisposable
        {
            private JobKey _jobKey;
            private readonly IScheduler _scheduler;

            public AsyncDisposable(IScheduler scheduler)
            {
                _scheduler = scheduler;
            }

            public void SetKey(JobKey jobKey)
            {
                _jobKey = jobKey;
            }

            public async ValueTask DisposeAsync()
            {
                if (_jobKey != null)
                {
                    await _scheduler.DeleteJob(_jobKey);
                }
            }
        }


        private readonly ISchedulerFactory _schedulerFactory;
        private readonly ILogger _logger;
        private IScheduler _scheduler;
        private readonly TaskSchedulerDictionary _jobSchedulerDictionary;
        private readonly IServiceProvider _serviceProvider;

        public TaskScheduler(
            ISchedulerFactory schedulerFactory,
            ILogger<TaskScheduler> logger,
            TaskSchedulerDictionary jobSchedulerDictionary,
             IServiceProvider serviceProvider
            )
        {
            _schedulerFactory = schedulerFactory;
            _logger = logger;
            _jobSchedulerDictionary = jobSchedulerDictionary;
            _serviceProvider = serviceProvider;
        }

        public async Task<IAsyncDisposable> ScheduleAsync<T>(
            JobSchedulerKey jobSchedulerKey,
            IReadOnlyCollection<ITrigger> triggers,
            IDictionary<string, object?> properties
            )
            where T : JobBase
        {
            _scheduler = await _schedulerFactory.GetScheduler();
            AsyncDisposable asyncDisposable = new AsyncDisposable(_scheduler);
            try
            {

                var jobType = typeof(T);
                IDictionary<string, object?> props = new Dictionary<string, object?>()
                {
                    {nameof(JobBase.Logger),_serviceProvider.GetService<ILogger<T>>()},
                    {nameof(JobBase.Properties),properties},
                    {nameof(JobBase.ServiceProvider),_serviceProvider},
                    {nameof(JobBase.TriggerSource),jobSchedulerKey.TriggerSource },
                    {nameof(JobBase.AsyncDispoable),jobSchedulerKey.TriggerSource
                    == JobTriggerSource.Manual? asyncDisposable:null }
                };

                IJobDetail job = JobBuilder.Create(jobType)
                    .SetJobData(new JobDataMap(props))
                    .Build();
                asyncDisposable.SetKey(job.Key);
                Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> jobsAndTriggers = [];
                jobsAndTriggers.Add(job, triggers);

                await _scheduler.ScheduleJobs(jobsAndTriggers, true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
            }
            return asyncDisposable;
        }

    }
}
