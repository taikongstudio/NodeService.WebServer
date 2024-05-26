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
        private readonly ApplicationRepositoryFactory<PropertyBag> _propertyBagRepoFactory;
        private readonly IMemoryCache _memoryCache;
        private readonly BatchQueue<DataQualityAlarmMessage> _alarmMessageBatchQueue;
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
            IMemoryCache memoryCache,
            BatchQueue<DataQualityAlarmMessage> alarmMessageBatchQueue
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
            _alarmMessageBatchQueue = alarmMessageBatchQueue;
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
                    while (dateTime >= _dataQualitySettings.StatisticsLimitDate)
                    {
                        await RefreshNodeSettingsAsync(stoppingToken);
                        if (_dataQualitySettings == null || !_dataQualitySettings.IsEnabled)
                        {
                            break;
                        }
                        var ipNodeInfoMapping = new ConcurrentDictionary<string, NodeInfoModel>(StringComparer.OrdinalIgnoreCase);
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
                int durationMinutes = 0;
                if (_dataQualitySettings == null)
                {
                    durationMinutes = 10;
                }
                else
                {
                    durationMinutes = _dataQualitySettings.DurationMinutes;
                }
                if (durationMinutes < 1)
                {
                    durationMinutes = 10;
                }
                await Task.Delay(TimeSpan.FromMinutes(durationMinutes), stoppingToken);
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
            ConcurrentDictionary<string, NodeInfoModel> nodeMapping,
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
                        using var dbContext = new DataQualityStatisticsDbContext(databaseProviderType, connectionString);
                        var querable = dbContext.Database.SqlQueryRaw<DataQualityNodeStatisticsItem>(
                            statisticsDefinition.Scripts, dateTime);
                        var results = await querable.ToListAsync(cancellationToken);
                        List<DataQualityNodeStatisticsRecordModel> addRecordList = [];
                        List<DataQualityNodeStatisticsRecordModel> updateRecordList = [];
                        List<NodeInfoModel> nodeList = [];
                        if (statisticsDefinition.NodeList != null && statisticsDefinition.NodeList.Count > 0)
                        {
                            nodeList = await nodeInfoRepo.ListAsync(new NodeInfoSpecification(
                               AreaTags.Any,
                               NodeStatus.All,
                               DataFilterCollection<string>.Includes(statisticsDefinition.NodeList.Select(x => x.Value))), cancellationToken);
                        }
                        var groups = results.GroupBy(x => x.name);

                        foreach (var nodeGroup in groups)
                        {
                            var name = nodeGroup.Key;
                            var item = nodeGroup.LastOrDefault();
                            if (item == null || name == null)
                            {
                                continue;
                            }
                            if (item.dt.Date != dateTime)
                            {
                                continue;
                            }
                            NodeInfoModel? nodeInfo = null;
                            if (name != null)
                            {
                                if (!nodeMapping.TryGetValue(name, out nodeInfo))
                                {
                                    nodeInfo = await nodeInfoRepo.FirstOrDefaultAsync(
                                            new NodeInfoSpecification(name, null),
                                            cancellationToken);
                                    if (nodeInfo == null)
                                    {
                                        continue;
                                    }
                                    nodeMapping.AddOrUpdate(name, nodeInfo, (key, oldValue) => nodeInfo);
                                }
                            }

                            if (nodeInfo == null)
                            {
                                continue;
                            }
                            _logger.LogInformation($"{dateTime}=>{statisticsDefinition.Name}=>{nodeInfo.Name}");
                            var value = (double?)item.sampling_rate;
                            await AddOrUpdateReportAsync(
                                dateTime,
                                statisticsRecordRepo,
                                statisticsDefinition.Name,
                                nodeInfo,
                                value,
                                addRecordList,
                                updateRecordList,
                                item.message,
                                cancellationToken);

                        }

                        if (nodeList.Count > 0)
                        {
                            foreach (var nodeInfo in nodeList)
                            {
                                bool found = false;
                                foreach (var item in groups)
                                {
                                    var name = item.Key;
                                    if (nodeInfo.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                                    {
                                        found = true;
                                        break;
                                    }
                                }
                                if (!found)
                                {
                                    await AddOrUpdateReportAsync(
                                        dateTime,
                                        statisticsRecordRepo,
                                        statisticsDefinition.Name,
                                        nodeInfo,
                                        null,
                                        addRecordList,
                                        updateRecordList,
                                        "no data",
                                        cancellationToken);


                                }
                            }
                        }

                        if (addRecordList.Count > 0)
                        {
                            await statisticsRecordRepo.AddRangeAsync(addRecordList, cancellationToken);
                        }
                        if (updateRecordList.Count > 0)
                        {
                            await statisticsRecordRepo.UpdateRangeAsync(updateRecordList, cancellationToken);
                        }
                        foreach (var record in addRecordList.Union(updateRecordList).Distinct())
                        {
                            foreach (var entry in record.Entries)
                            {
                                if (entry.Value == null || entry.Value != 1)
                                {
                                    DataQualityAlarmMessage dataQualityAlarmMessage = new DataQualityAlarmMessage();
                                    dataQualityAlarmMessage.MachineName = record.Name;
                                    dataQualityAlarmMessage.DataSource = entry.Name;
                                    dataQualityAlarmMessage.Message = entry.Message;
                                    dataQualityAlarmMessage.DateTime = dateTime;
                                    await _alarmMessageBatchQueue.SendAsync(dataQualityAlarmMessage);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _exceptionCounter.AddOrUpdate(ex);
                        _logger.LogError(ex.ToString());
                    }
                    finally
                    {
                        statisticsRecordRepo.DbContext.ChangeTracker.Clear();
                    }


                }
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
        }

        private async Task AddOrUpdateReportAsync(
            DateTime dateTime,
            IRepository<DataQualityNodeStatisticsRecordModel> statisticsRecordRepo,
            string name,
            NodeInfoModel nodeInfo,
            double? value,
            List<DataQualityNodeStatisticsRecordModel> addRecordList,
            List<DataQualityNodeStatisticsRecordModel> updateRecordList,
            string? message,
            CancellationToken cancellationToken = default)
        {
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
            AddOrUpdateEntry(
                report,
                name,
                value,
                message);
            updateRecordList.Add(report);
        }

        private static void AddOrUpdateEntry(DataQualityNodeStatisticsRecordModel report, string key, double? samplingRate, string? message = null)
        {
            var entry = report.Value.Entries.FirstOrDefault(x => x.Name == key);
            var value = samplingRate;
            if (value < 0)
            {
                value = null;
            }
            if (entry == null)
            {
                entry = new DataQualityNodeStatisticsEntry()
                {
                    Name = key,
                    Value = value,
                    Message = message
                };
                report.Value.Entries.Add(entry);
            }
            else
            {
                entry.Value = value;
                entry.Message = message;
            }
        }
    }
}
