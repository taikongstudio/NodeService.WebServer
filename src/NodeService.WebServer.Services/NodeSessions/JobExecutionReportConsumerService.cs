﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Logging;
using NodeService.Infrastructure.Messages;
using NodeService.WebServer.Data;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Tasks;
using NodeService.WebServer.Services.VirtualSystem;
using Quartz;
using Quartz.Util;
using System.Composition;
using System.Globalization;
using System.Linq;
using static NodeService.Infrastructure.Models.JobExecutionReport.Types;

namespace NodeService.WebServer.Services.NodeSessions
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

            await foreach (var arrayPoolCollection in _jobExecutionReportBatchQueue.ReceiveAllAsync(stoppingToken))
            {
                int count = arrayPoolCollection.CountNotNull();
                if (count == 0)
                {
                    continue;
                }

                Stopwatch stopwatch = new Stopwatch();

                try
                {

                    stopwatch.Start();
                    await ProcessJobExecutionReportsAsync(arrayPoolCollection, stoppingToken);
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

        private static string GetId(JobExecutionReport report)
        {
            if (report.Properties.TryGetValue("Id", out var id))
            {
                return id;
            }
            return report.Id;
        }

        private async Task ProcessJobExecutionReportsAsync(
            ArrayPoolCollection<JobExecutionReportMessage> arrayPoolCollection,
            CancellationToken stoppingToken = default)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var stopwatchSave = new Stopwatch();
            var timeSpan = TimeSpan.Zero;
            try
            {

                foreach (var instanceMessageGroup in arrayPoolCollection
                    .Where(static x => x != null)
                    .GroupBy(static x => GetId(x.GetMessage())))
                {
                    if (instanceMessageGroup == null)
                    {
                        continue;
                    }
                    Stopwatch stopwatchQuery = Stopwatch.StartNew();
                    var id = instanceMessageGroup.Key;
                    var jobExecutionInstance = await dbContext.FindAsync<JobExecutionInstanceModel>(id);
                    stopwatchQuery.Stop();
                    _logger.LogInformation($"Query {id}:{stopwatchQuery.Elapsed}");

                    if (jobExecutionInstance == null)
                    {
                        continue;
                    }

                    var logPersistenceGroup = new LogPersistenceGroup()
                    {
                        Id = id,
                    };


                    foreach (var reportMessage in instanceMessageGroup)
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

                    if (logPersistenceGroup.LogMessageEntries.Count > 0)
                    {
                        _logPersistenceBatchQueue.Post(logPersistenceGroup);

                        _logger.LogInformation($"Post group:{id}");
                    }

                    Stopwatch stopwatchProcessMessage = new Stopwatch();
                    foreach (var messageStatusGroup in instanceMessageGroup.GroupBy(static x => x.GetMessage().Status))
                    {

                        stopwatchProcessMessage.Start();

                        foreach (var reportMessage in messageStatusGroup)
                        {
                            await ProcessJobExecutionReportAsync(jobExecutionInstance, reportMessage, stoppingToken);
                        }

                        stopwatchProcessMessage.Stop();
                        _logger.LogInformation($"process:{messageStatusGroup.Count()},spent:{stopwatchProcessMessage.Elapsed}");
                        stopwatchProcessMessage.Reset();
                        stopwatchSave.Start();
                        await dbContext.SaveChangesAsync(stoppingToken);
                        stopwatchSave.Stop();
                    }

                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
            }
            finally
            {
                _logger.LogInformation($"Process {arrayPoolCollection.CountNotNull()} messages, SaveElapsed:{stopwatchSave.Elapsed}");

            }
        }

        private async ValueTask CancelExecutionTimeLimitJob(JobSchedulerKey jobSchedulerKey)
        {
            if (!_jobSchedulerDictionary.TryRemove(jobSchedulerKey, out IAsyncDisposable? asyncDisposable))
            {
                return;
            }
            if (asyncDisposable != null)
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
            _jobSchedulerDictionary.TryAdd(key, asyncDisposable);
        }

        private async Task ProcessJobExecutionReportAsync(
            JobExecutionInstanceModel jobExecutionInstance,
            NodeSessionMessage<JobExecutionReport> message,
            CancellationToken stoppingToken = default)
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
                        await ScheduleChildJobs(jobExecutionInstance.Id, stoppingToken);
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
                        jobExecutionInstance.ExecutionBeginTimeUtc = DateTime.UtcNow;
                        break;
                    case JobExecutionStatus.Running:
                        break;
                    case JobExecutionStatus.Failed:
                    case JobExecutionStatus.Finished:
                    case JobExecutionStatus.Cancelled:
                        jobExecutionInstance.ExecutionEndTimeUtc = DateTime.UtcNow;
                        break;
                    default:
                        break;
                }

                jobExecutionInstance.Status = report.Status;
                if (report.Message != null)
                {
                    jobExecutionInstance.Message = report.Message;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
            }


        }


        private async Task ScheduleChildJobs(
            string id,
            CancellationToken stoppingToken = default)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var jobScheduleConfig = await dbContext
                .JobScheduleConfigurationDbSet
                .FindAsync(id);
            if (jobScheduleConfig == null)
            {
                return;
            }
            foreach (var childJob in jobScheduleConfig.ChildJobs)
            {
                var childJobScheduleConfig = await dbContext.JobScheduleConfigurationDbSet.FindAsync(childJob.Value);
                if (childJobScheduleConfig == null)
                {
                    continue;
                }
                await _jobScheduleAsyncQueue.EnqueueAsync(new(JobTriggerSource.Parent, childJobScheduleConfig.Id));
            }
        }
    }
}
