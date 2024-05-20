using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.DataQuality
{
    public class DataQualityAlarmService : BackgroundService
    {
        private readonly ILogger<DataQualityAlarmService> _logger;
        private readonly ExceptionCounter _exceptionCounter;
        private readonly ApplicationRepositoryFactory<DataQualityNodeStatisticsRecordModel> _statisticsRecordRepoFactory;

        public DataQualityAlarmService(
            ILogger<DataQualityAlarmService> logger,
            ExceptionCounter exceptionCounter,
            ApplicationRepositoryFactory<DataQualityNodeStatisticsRecordModel> statisticsRecordRepoFactory)
        {
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _statisticsRecordRepoFactory = statisticsRecordRepoFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                //using var statisticsRecordRepo = _statisticsRecordRepoFactory.CreateRepository();
                //DateTime dateTime = DateTime.Now.Date;
                //while (dateTime > DateTime.Now.Date.AddYears(-365))
                //{

                //    await ScanRecordsAsync(statisticsRecordRepo, dateTime, stoppingToken);
                //    dateTime = dateTime.AddDays(-1);
                //}
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }

        private async Task ScanRecordsAsync(
            IRepository<DataQualityNodeStatisticsRecordModel> statisticsRecordRepo,
            DateTime dateTime,
            CancellationToken cancellationToken = default)
        {
            try
            {
                DateTime beginDateTime = dateTime.AddDays(-1);
                DateTime endDateTime = dateTime.AddSeconds(-1);
                var records = await statisticsRecordRepo.ListAsync(
                    new DataQualityStatisticsSpecification(beginDateTime, endDateTime),
                    cancellationToken);
                List<DataQualityNodeStatisticsRecordModel> recordList = [];
                foreach (var record in records)
                {
                    foreach (var entry in record.Entries)
                    {

                        if (entry.Value == null)
                        {
                            recordList.Add(record);
                        }
                        else
                        {
                            var value = entry.Value.GetValueOrDefault();
                            if (value < 1)
                            {
                                recordList.Add(record);
                            }
                        }
                    }
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
