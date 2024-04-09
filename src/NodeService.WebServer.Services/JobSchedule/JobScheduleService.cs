using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Config;
using NodeService.Infrastructure.DataModels;
using NodeService.WebServer.Data;
using System;

namespace NodeService.WebServer.Services.JobSchedule
{
    public class JobScheduleService : BackgroundService
    {

        private readonly IAsyncQueue<JobScheduleMessage> _jobSchedulerMessageQueue;
        private readonly IServiceProvider _serviceProvider;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ILogger<JobScheduleService> _logger;
        private readonly ISchedulerFactory _schedulerFactory;
        private IScheduler Scheduler;
        private readonly JobSchedulerDictionary _jobSchedulerDictionary;

        public JobScheduleService(
            IServiceProvider serviceProvider,
            IAsyncQueue<JobScheduleMessage> jobScheduleMessageQueue,
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            ISchedulerFactory schedulerFactory,
            JobSchedulerDictionary jobSchedulerDictionary,
            ILogger<JobScheduleService> logger)
        {
            _serviceProvider = serviceProvider;
            _dbContextFactory = dbContextFactory;
            _logger = logger;
            _schedulerFactory = schedulerFactory;
            _jobSchedulerDictionary = jobSchedulerDictionary;
            _jobSchedulerMessageQueue = jobScheduleMessageQueue;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Scheduler = await _schedulerFactory.GetScheduler(stoppingToken);
            if (!Scheduler.IsStarted)
            {
                await Scheduler.Start(stoppingToken);
            }

            await ScheduleJobsFromDbContext();
            while (!stoppingToken.IsCancellationRequested)
            {
                var jobScheduleMessage = await _jobSchedulerMessageQueue.DeuqueAsync(stoppingToken);
                try
                {
                    await ScheduleAsync(jobScheduleMessage);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.ToString());
                }

            }
        }

        private async ValueTask ScheduleAsync(JobScheduleMessage jobScheduleMessage)
        {
            if (jobScheduleMessage.JobScheduleConfigId == null)
            {
                return;
            }
            using var dbContext = _dbContextFactory.CreateDbContext();
            var jobScheduleConfig = await dbContext
                .JobScheduleConfigurationDbSet
                .AsQueryable()
                .FirstOrDefaultAsync(x => x.Id == jobScheduleMessage.JobScheduleConfigId);
            if (jobScheduleConfig == null)
            {
                await CancelJobAsync(jobScheduleMessage.JobScheduleConfigId);
                return;
            }
            var jobSchedulerKey = new JobSchedulerKey(jobScheduleMessage.JobScheduleConfigId, jobScheduleMessage.TriggerSource);
            if (_jobSchedulerDictionary.TryGetValue(jobSchedulerKey, out var asyncDisposable))
            {
                await asyncDisposable.DisposeAsync();
                if (!jobScheduleConfig.IsEnabled || jobScheduleMessage.IsCancellationRequested)
                {
                    _jobSchedulerDictionary.TryRemove(jobSchedulerKey, out _);
                    return;
                }
                var newAsyncDisposable = await ScheduleJobAsync(jobSchedulerKey, jobScheduleConfig);
                _jobSchedulerDictionary.TryUpdate(jobSchedulerKey, newAsyncDisposable, asyncDisposable);
            }
            else if (!jobScheduleMessage.IsCancellationRequested)
            {
                asyncDisposable = await ScheduleJobAsync(jobSchedulerKey, jobScheduleConfig);
                _jobSchedulerDictionary.TryAdd(jobSchedulerKey, asyncDisposable);
            }

        }

        private async ValueTask CancelJobAsync(string key)
        {
            for (JobTriggerSource i = JobTriggerSource.Schedule; i < JobTriggerSource.Max - 1; i++)
            {
                var jobSchedulerKey = new JobSchedulerKey(key, JobTriggerSource.Schedule);
                if (_jobSchedulerDictionary.TryRemove(jobSchedulerKey, out var asyncDisposable))
                {
                    await asyncDisposable.DisposeAsync();
                }
            }
        }

        private async ValueTask ScheduleJobsFromDbContext()
        {
            try
            {
                using var dbContext = _dbContextFactory.CreateDbContext();
                var jobScheduleConfigs = await dbContext.JobScheduleConfigurationDbSet
                    .AsAsyncEnumerable()
                    .Where(x => x.IsEnabled && x.TriggerType == JobScheduleTriggerType.Schedule)
                    .ToListAsync();
                foreach (var jobScheduleConfig in jobScheduleConfigs)
                {
                    await _jobSchedulerMessageQueue.EnqueueAsync(new(JobTriggerSource.Schedule, jobScheduleConfig.Id));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
            }

        }

        private async ValueTask<IAsyncDisposable> ScheduleJobAsync(
            JobSchedulerKey jobSchedulerKey,
            JobScheduleConfigModel jobScheduleConfig)
        {

            var jobScheduler = _serviceProvider.GetService<JobScheduler>();
            var asyncDisposable = await jobScheduler.ScheduleAsync<FireJob>(jobSchedulerKey,
                 jobSchedulerKey.TriggerSource == JobTriggerSource.Schedule ?
                 TriggerBuilderHelper.BuildScheduleTrigger(jobScheduleConfig.CronExpressions.Select(x => x.Value)) :
                 TriggerBuilderHelper.BuildStartNowTrigger(),
                 new Dictionary<string, object?>()
                 {
                    {
                        "JobScheduleConfig",
                        jobScheduleConfig
                    }
                 }
             );
            return asyncDisposable;
        }


    }
}
