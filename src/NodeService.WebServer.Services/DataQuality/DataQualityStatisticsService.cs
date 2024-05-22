using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.DataModels;
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
        private readonly ApplicationRepositoryFactory<PropertyBag> _propertyBagRepoFactory;
        private readonly IMemoryCache _memoryCache;
        readonly ILogger<DataQualityStatisticsService> _logger;
        readonly ExceptionCounter _exceptionCounter;
        private DataQualitySettings? _dataQualitySettings;

        public DataQualityStatisticsService(
            ILogger<DataQualityStatisticsService> logger,
            ApplicationRepositoryFactory<DatabaseConfigModel> databaseConfigRepoFactory,
            ApplicationRepositoryFactory<DataQualityStatisticsDefinitionModel> statisticsDefinitionRepoFactory,
            ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepoFactory,
            ApplicationRepositoryFactory<DataQualityNodeStatisticsRecordModel> statisticsRecordRepoFactory,
            ApplicationRepositoryFactory<PropertyBag> propertyBagRepoFactory,
            ExceptionCounter exceptionCounter,
            IMemoryCache memoryCache
            )
        {
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _statisticsDefinitionRepoFactory = statisticsDefinitionRepoFactory;
            _databaseConfigRepoFactory = databaseConfigRepoFactory;
            _nodeInfoRepoFactory = nodeInfoRepoFactory;
            _statisticsRecordRepoFactory = statisticsRecordRepoFactory;
            _propertyBagRepoFactory = propertyBagRepoFactory;
            _memoryCache = memoryCache;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    DateTime dateTime = DateTime.Today.Date;
                    await RefreshNodeSettingsAsync(stoppingToken);
                    if (_dataQualitySettings == null || !_dataQualitySettings.IsEnabled)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                        continue;
                    }
                    var ipNodeInfoMapping = new ConcurrentDictionary<string, NodeInfoModel>();
                    while (dateTime > _dataQualitySettings.StatisticsLimitDate)
                    {
                        await StatisticsDateAysnc(dateTime, ipNodeInfoMapping, stoppingToken);
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                        dateTime = dateTime.AddDays(-1);
                    }

                }
                catch (Exception ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }



                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
        }

        private async Task RefreshNodeSettingsAsync(CancellationToken cancellationToken = default)
        {
            using var propertyBagRepo = _propertyBagRepoFactory.CreateRepository();
            var propertyBag = await propertyBagRepo.FirstOrDefaultAsync(
                new PropertyBagSpecification(nameof(DataQualitySettings)),
                cancellationToken);
            if (propertyBag == null || !propertyBag.TryGetValue("Value", out var value))
                _dataQualitySettings = new DataQualitySettings();
            else
                _dataQualitySettings = JsonSerializer.Deserialize<DataQualitySettings>(value as string);
        }

        private async Task StatisticsDateAysnc(
            DateTime dateTime,
            ConcurrentDictionary<string, NodeInfoModel> ipNamesMapping,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation($"{dateTime}");
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
                        //if (!_memoryCache.TryGetValue<DataQuanlityStatisticsServiceStatus>(nameof(DataQuanlityStatisticsServiceStatus), out var status))
                        //{
                        //    status = new DataQuanlityStatisticsServiceStatus();
                        //    status.IsRunning = true;
                        //    status.Message = $"正在分析指标：{statisticsDefinition.Name}，{dateTime:yyyy-MM-dd}的数据";
                        //    _memoryCache.Set(status, TimeSpan.FromMinutes(1));
                        //}
                        using var dbContext = new DataQualityStatisticsDbContext(databaseProviderType, connectionString);
                        var querable = dbContext.Database.SqlQueryRaw<DataQualityNodeStatisticsItem>(
                            statisticsDefinition.Scripts);
                        var results = await querable.ToListAsync(cancellationToken);
                        List<DataQualityNodeStatisticsRecordModel> addRecordList = [];
                        List<DataQualityNodeStatisticsRecordModel> updateRecordList = [];
                        foreach (var item in results)
                        {
                            string ipAddress = item.ip;
                            if (ipAddress == "127.0.0.1")
                            {
                                ipAddress = "::1";
                            }
                            if (!ipNamesMapping.TryGetValue(ipAddress, out var nodeInfo))
                            {
                                nodeInfo = await nodeInfoRepo.FirstOrDefaultAsync(
                                        new NodeInfoSpecification(null, ipAddress),
                                        cancellationToken);
                                if (nodeInfo == null)
                                {
                                    continue;
                                }
                                ipNamesMapping.AddOrUpdate(ipAddress, nodeInfo, (key, oldValue) => nodeInfo);
                            }
                            _logger.LogInformation($"{dateTime}=>{statisticsDefinition.Name}=>{nodeInfo.Name}");
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
