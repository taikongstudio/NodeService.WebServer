using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Services.DataQuality
{
    public class DataQualityStatisticsService : BackgroundService
    {
        readonly ApplicationRepositoryFactory<DataQualityStatisticsDefinitionModel> _statisticsDefinitionRepoFactory;
        readonly ApplicationRepositoryFactory<DatabaseConfigModel> _databaseConfigRepoFactory;
        readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepoFactory;
        readonly ApplicationRepositoryFactory<DataQualityNodeStatisticsRecordModel> _statisticsRecordRepoFactory;
        readonly ILogger<DataQualityStatisticsService> _logger;
        readonly ExceptionCounter _exceptionCounter;

        public DataQualityStatisticsService(
            ILogger<DataQualityStatisticsService> logger,
            ApplicationRepositoryFactory<DatabaseConfigModel> databaseConfigRepoFactory,
            ApplicationRepositoryFactory<DataQualityStatisticsDefinitionModel> statisticsDefinitionRepoFactory,
            ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepoFactory,
            ApplicationRepositoryFactory<DataQualityNodeStatisticsRecordModel> statisticsRecordRepoFactory,
            ExceptionCounter exceptionCounter
            )
        {
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _statisticsDefinitionRepoFactory = statisticsDefinitionRepoFactory;
            _databaseConfigRepoFactory = databaseConfigRepoFactory;
            _nodeInfoRepoFactory = nodeInfoRepoFactory;
            _statisticsRecordRepoFactory = statisticsRecordRepoFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (true)
            {
                await StatisticsAysnc(stoppingToken);
                await Task.Delay(TimeSpan.FromMinutes(10));
            }
        }

        private async Task StatisticsAysnc(CancellationToken cancellationToken = default)
        {
            try
            {
                using var statisticsDefinitionRepo = _statisticsDefinitionRepoFactory.CreateRepository();
                using var databaseConfigRepo = _databaseConfigRepoFactory.CreateRepository();
                using var nodeInfoRepo = _nodeInfoRepoFactory.CreateRepository();
                using var statisticsRecordRepo = _statisticsRecordRepoFactory.CreateRepository();
                var statisticsDefinitions = await statisticsDefinitionRepo.ListAsync(cancellationToken);
                foreach (var statisticsDefinition in statisticsDefinitions.Where(static x => x.IsEnabled))
                {
                    try
                    {

                        if (statisticsDefinition.DatabaseConfig == null)
                        {
                            statisticsDefinition.DatabaseConfig = await databaseConfigRepo.GetByIdAsync(
                                statisticsDefinition.DatabaseConfigId,
                                cancellationToken);
                        }
                        if (statisticsDefinition.DatabaseConfig == null)
                        {
                            continue;
                        }
                        if (!statisticsDefinition.DatabaseConfig.TryBuildConnectionString(
                            out var databaseProviderType,
                            out var connectionString)
                            ||
                            connectionString == null
                            )
                        {
                            continue;
                        }
                        using var dbContext = new DataQualityStatisticsDbContext(databaseProviderType, connectionString);
                        var querable = dbContext.Database.SqlQueryRaw<DataQualityNodeStatisticsItem>(
                            statisticsDefinition.Scripts);
                        var results = await querable.ToListAsync(cancellationToken);
                        DateTime dateTime = DateTime.Now.Date.AddDays(-1);
                        List<DataQualityNodeStatisticsRecordModel> addRecordList = [];
                        List<DataQualityNodeStatisticsRecordModel> updateRecordList = [];
                        foreach (var item in results)
                        {
                            string ipAddress = item.ip;
                            if (ipAddress == "127.0.0.1")
                            {
                                ipAddress = "::1";
                            }
                            var nodeInfo = await nodeInfoRepo.FirstOrDefaultAsync(
                                new NodeInfoSpecification(null, ipAddress),
                                cancellationToken);
                            if (nodeInfo == null)
                            {
                                continue;
                            }
                            string key = $"{nodeInfo.Id}-{dateTime:yyyyMMdd}";
                            var report = await statisticsRecordRepo.FirstOrDefaultAsync(
                                new DataQualityStatisticsSpecification(key, dateTime),
                                cancellationToken);
                            if (report == null)
                            {
                                report = new DataQualityNodeStatisticsRecordModel()
                                {
                                    Id = key,
                                    NodeId = nodeInfo.Id,
                                    CreationDateTime = dateTime,
                                    Name = nodeInfo.Name,
                                };
                                addRecordList.Add(report);
                            }
                            report.DateTime = dateTime;
                            var entry = report.Value.Entries.FirstOrDefault(x => x.Name == statisticsDefinition.Name);
                            if (entry == null)
                            {
                                entry = new DataQualityNodeStatisticsEntry()
                                {
                                    Name = statisticsDefinition.Name,
                                    Value = ((double?)item.sampling_rate),
                                };
                                report.Value.Entries.Add(entry);
                            }
                            else
                            {
                                entry.Value = (double?)item.sampling_rate;
                            }
                            updateRecordList.Add(report);


                        }
                        if (addRecordList.Count > 0)
                        {
                            await statisticsRecordRepo.AddRangeAsync(addRecordList, cancellationToken);
                        }
                        if (updateRecordList.Count > 0)
                        {
                            await statisticsRecordRepo.UpdateRangeAsync(updateRecordList, cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        _exceptionCounter.AddOrUpdate(ex);
                        _logger.LogError(ex.ToString());
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
