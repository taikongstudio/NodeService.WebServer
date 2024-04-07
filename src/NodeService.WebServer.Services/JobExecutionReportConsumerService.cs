using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Logging;
using NodeService.Infrastructure.Messages;
using NodeService.WebServer.Data;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.JobSchedule;
using NodeService.WebServer.Services.NodeSessions;
using NodeService.WebServer.Services.VirtualSystem;
using Quartz;
using Quartz.Util;
using System.Composition;
using System.Globalization;
using static NodeService.Infrastructure.Models.JobExecutionReport.Types;

namespace NodeService.WebServer.Services
{
    public class JobExecutionReportConsumerService : BackgroundService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly BatchQueue<JobExecutionReportMessage> _jobExecutionReportBatchQueue;
        private readonly IAsyncQueue<JobScheduleMessage> _jobScheduleAsyncQueue;
        private readonly ILogger<JobExecutionReportConsumerService> _logger;
        private readonly JobSchedulerDictionary _jobSchedulerDictionary;
        private readonly JobScheduler _jobScheduler;
        private readonly BatchQueue<LogPersistenceGroup> _logPersistenceBatchQueue;

        public JobExecutionReportConsumerService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            BatchQueue<JobExecutionReportMessage> jobExecutionReportBatchQueue,
            BatchQueue<LogPersistenceGroup> logPersistenceBatchQueue,
            IAsyncQueue<JobScheduleMessage> jobScheduleAsyncQueue,
            ILogger<JobExecutionReportConsumerService> logger,
            JobSchedulerDictionary jobSchedulerDictionary,
            JobScheduler jobScheduler
            )
        {
            _dbContextFactory = dbContextFactory;
            _jobExecutionReportBatchQueue = jobExecutionReportBatchQueue;
            _jobScheduleAsyncQueue = jobScheduleAsyncQueue;
            _logger = logger;
            _jobSchedulerDictionary = jobSchedulerDictionary;
            _jobScheduler = jobScheduler;
            _logPersistenceBatchQueue = logPersistenceBatchQueue;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {

            await foreach (var arrayPoolCollection in _jobExecutionReportBatchQueue.ReceiveAllAsync(stoppingToken)
                                                                                   .ConfigureAwait(false))
            {
                Stopwatch stopwatch = new Stopwatch();
                int count = 0;
                try
                {
                    count = arrayPoolCollection.CountNotNull();
                    if (count == 0)
                    {
                        continue;
                    }

                    stopwatch.Start();
                    await ProcessJobExecutionReportsAsync(arrayPoolCollection, stoppingToken).ConfigureAwait(false);
                    stopwatch.Stop();
                    _logger.LogInformation($"process {count} messages,spent: {stopwatch.Elapsed}, AvailableCount:{_jobExecutionReportBatchQueue.AvailableCount}");
                    stopwatch.Reset();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.ToString());
                }
                finally
                {
                    stopwatch.Reset();
                    arrayPoolCollection.Dispose();
                }
            }
        }

        private async Task ProcessJobExecutionReportsAsync(ArrayPoolCollection<JobExecutionReportMessage> arrayPoolCollection,
                                                           CancellationToken stoppingToken = default)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var stopwatchSave = new Stopwatch();
            var timeSpan = TimeSpan.Zero;
            try
            {

                foreach (var messageGroup in arrayPoolCollection
                    .Where(static x => x != null)
                    .GroupBy(x => x.GetMessage().Properties[nameof(JobExecutionInstanceModel.Id)]))
                {
                    if (messageGroup == null)
                    {
                        continue;
                    }
                    Stopwatch stopwatchQuery = Stopwatch.StartNew();
                    var jobExecutionInstanceId = messageGroup.Key;
                    var jobExecutionInstance = await dbContext.FindAsync<JobExecutionInstanceModel>(jobExecutionInstanceId).ConfigureAwait(false);
                    stopwatchQuery.Stop();
                    _logger.LogInformation($"Query {jobExecutionInstance.Id}:{stopwatchQuery.Elapsed}");

                    var logPersistenceGroup = new LogPersistenceGroup()
                    {
                        Id = jobExecutionInstanceId,
                    };


                    foreach (var reportMessage in messageGroup)
                    {

                        var report = reportMessage.GetMessage();
                        if (report.LogEntries.Count > 0)
                        {
                            var logMessageEntries = report.LogEntries.Select(x => new LogMessageEntry()
                            {
                                //Id = jobExecutionInstanceId,
                                DateTime = x.DateTime.ToDateTime(),
                                Type = (int)x.Type,
                                Value = x.Value,
                                Status = report.Status
                            });
                            logPersistenceGroup.LogMessageEntries.AddRange(logMessageEntries);
                        }

                    }

                    this._logPersistenceBatchQueue.Post(logPersistenceGroup);

                    _logger.LogInformation($"Post group:{jobExecutionInstanceId}");


                    Stopwatch stopwatchProcessMessage = new Stopwatch();
                    foreach (var messageStatusGroup in messageGroup.GroupBy(static x => x.GetMessage().Status))
                    {

                        stopwatchProcessMessage.Start();

                        foreach (var reportMessage in messageStatusGroup)
                        {
                            await ProcessJobExecutionReportAsync(jobExecutionInstance, reportMessage, stoppingToken).ConfigureAwait(false);
                        }

                        stopwatchProcessMessage.Stop();
                        _logger.LogInformation($"process:{messageStatusGroup.Count()},spent:{stopwatchProcessMessage.Elapsed}");
                        stopwatchProcessMessage.Reset();
                        stopwatchSave.Start();
                        await dbContext.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
                        stopwatchSave.Stop();
                        timeSpan += stopwatchSave.Elapsed;
                        stopwatchSave.Reset();
                    }

                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
            }
            finally
            {
                this._logger.LogInformation($"Process {arrayPoolCollection.CountNotNull()} messages, SaveElapsed:{stopwatchSave.Elapsed}");

            }
        }

        private async ValueTask CancelExecutionTimeLimitJob(JobSchedulerKey jobSchedulerKey)
        {
            if (this._jobSchedulerDictionary.TryRemove(jobSchedulerKey, out IAsyncDisposable  asyncDisposable))
            {
                await asyncDisposable.DisposeAsync();
            }
        }

        private async Task ScheduleExecutionTimeLimitJob(JobExecutionInstanceModel jobExecutionInstance)
        {
            if (jobExecutionInstance == null)
            {
                return;
            }
            using var dbContext = _dbContextFactory.CreateDbContext();
            var jobScheduleConfig = await dbContext.JobScheduleConfigurationDbSet.FindAsync(jobExecutionInstance.JobScheduleConfigId);
            if (jobScheduleConfig == null)
            {
                return;
            }
            if (jobScheduleConfig.ExecutionLimitTimeSeconds <= 0)
            {
                return;
            }
            var key = new JobSchedulerKey(
                 jobExecutionInstance.Id,
                 JobTriggerSource.Schedule);
            var asyncDisposable = await _jobScheduler.ScheduleAsync<ExecutionTimeLimitJob>(key,
                 TriggerBuilderHelper.BuildStartAtTrigger(TimeSpan.FromSeconds(jobScheduleConfig.ExecutionLimitTimeSeconds)),
                new Dictionary<string, object?>(){
                    { "JobExecutionInstance",
                    jobExecutionInstance.JsonClone<JobExecutionInstanceModel>() }
                });
            this._jobSchedulerDictionary.TryAdd(key, asyncDisposable);
        }

        private async Task ProcessJobExecutionReportAsync(
            JobExecutionInstanceModel jobExecutionInstance,
            NodeSessionMessage<JobExecutionReport> message,
            CancellationToken stoppingToken=default)
        {
            try
            {
                JobExecutionReport report = message.GetMessage();


                var jobSchedulerKey = new JobSchedulerKey(jobExecutionInstance.Id, JobTriggerSource.Schedule);
                switch (report.Status)
                {
                    case JobExecutionStatus.Unknown:
                        break;
                    case JobExecutionStatus.Triggered:
                        break;
                    case JobExecutionStatus.Pendding:
                        break;
                    case JobExecutionStatus.Started:
                        await ScheduleExecutionTimeLimitJob(jobExecutionInstance);
                        break;
                    case JobExecutionStatus.Running:
                        break;
                    case JobExecutionStatus.Failed:
                        await CancelExecutionTimeLimitJob(jobSchedulerKey);
                        break;
                    case JobExecutionStatus.Finished:
                        await ScheduleChildJobs(jobExecutionInstance, stoppingToken);
                        await CancelExecutionTimeLimitJob(jobSchedulerKey);
                        break;
                    case JobExecutionStatus.Cancelled:
                        await CancelExecutionTimeLimitJob(jobSchedulerKey);
                        break;
                    case JobExecutionStatus.PenddingTimeout:
                        break;
                    default:
                        break;
                }


                switch (report.Status)
                {
                    case JobExecutionStatus.Unknown:
                        break;
                    case JobExecutionStatus.Triggered:
                    case JobExecutionStatus.Pendding:
                        break;
                    case JobExecutionStatus.Started:
                        if (report.Properties.TryGetValue(nameof(JobExecutionReport.CreatedDateTime), out var beginDateTimeString)
                            && DateTime.TryParse(beginDateTimeString, out var beginDateTime)
                            )
                        {
                            jobExecutionInstance.ExecutionBeginTime = beginDateTime;
                        }
                        else
                        {
                            jobExecutionInstance.ExecutionBeginTime = DateTime.UtcNow;
                        }

                        break;
                    case JobExecutionStatus.Running:
                        break;
                    case JobExecutionStatus.Failed:
                    case JobExecutionStatus.Finished:
                    case JobExecutionStatus.Cancelled:
                        if (report.Properties.TryGetValue(nameof(JobExecutionReport.CreatedDateTime), out var endDateTimeString)
                            && DateTime.TryParse(endDateTimeString, out var endDateTime)
                            )
                        {
                            jobExecutionInstance.ExecutionEndTime = endDateTime;
                        }
                        else
                        {
                            jobExecutionInstance.ExecutionEndTime = DateTime.UtcNow;
                        }
                        break;
                    default:
                        break;
                }

                jobExecutionInstance.Status = report.Status;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
            }


        }


        private async Task ScheduleChildJobs(JobExecutionInstanceModel jobExecutionInstance, CancellationToken stoppingToken = default)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var jobScheduleConfig = await dbContext.JobScheduleConfigurationDbSet.FindAsync(jobExecutionInstance.JobScheduleConfigId);
            await dbContext.Entry(jobExecutionInstance).ReloadAsync(stoppingToken);

            if (jobScheduleConfig == null)
            {
                return;
            }
            foreach (var binding in jobScheduleConfig.ChildJobs)
            {
                var config = await dbContext.JobScheduleConfigurationDbSet.FindAsync(binding.Value);
                if (config == null)
                {
                    continue;
                }
                await _jobScheduleAsyncQueue.EnqueueAsync(new JobScheduleMessage()
                {
                    JobScheduleConfigId = config.Id,
                    TriggerSource = JobTriggerSource.Parent,
                });
            }
        }
    }
}
