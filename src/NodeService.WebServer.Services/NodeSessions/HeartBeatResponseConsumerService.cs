using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Services.NodeSessions;

public class HeartBeatResponseConsumerService : BackgroundService
{
    readonly ExceptionCounter _exceptionCounter;
    readonly BatchQueue<NodeHeartBeatSessionMessage> _hearBeatMessageBatchQueue;
    readonly ILogger<HeartBeatResponseConsumerService> _logger;
    readonly IMemoryCache _memoryCache;
    readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepositoryFactory;
    readonly ApplicationRepositoryFactory<NodePropertySnapshotModel> _nodePropertyRepositoryFactory;
    readonly INodeSessionService _nodeSessionService;
    readonly ApplicationRepositoryFactory<PropertyBag> _propertyBagRepositoryFactory;
    readonly ApplicationRepositoryFactory<JobScheduleConfigModel> _taskDefinitionRepositoryFactory;
    readonly WebServerCounter _webServerCounter;
    readonly WebServerOptions _webServerOptions;
    NodeSettings _nodeSettings;

    public HeartBeatResponseConsumerService(
        ExceptionCounter exceptionCounter,
        ILogger<HeartBeatResponseConsumerService> logger,
        INodeSessionService nodeSessionService,
        ApplicationRepositoryFactory<JobScheduleConfigModel> taskDefinitionRepositoryFactory,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepositoryFactory,
        ApplicationRepositoryFactory<PropertyBag> propertyBagRepositoryFactory,
        ApplicationRepositoryFactory<NodePropertySnapshotModel> nodePropertyRepositoryFactory,
        BatchQueue<NodeHeartBeatSessionMessage> heartBeatMessageBatchBlock,
        IMemoryCache memoryCache,
        IOptionsMonitor<WebServerOptions> optionsMonitor,
        WebServerCounter webServerCounter
    )
    {
        _logger = logger;
        _nodeSessionService = nodeSessionService;
        _taskDefinitionRepositoryFactory = taskDefinitionRepositoryFactory;
        _nodeInfoRepositoryFactory = nodeInfoRepositoryFactory;
        _hearBeatMessageBatchQueue = heartBeatMessageBatchBlock;
        _propertyBagRepositoryFactory = propertyBagRepositoryFactory;
        _nodePropertyRepositoryFactory = nodePropertyRepositoryFactory;
        _memoryCache = memoryCache;
        _webServerOptions = optionsMonitor.CurrentValue;
        _nodeSettings = new NodeSettings();
        _exceptionCounter = exceptionCounter;
        _webServerCounter = webServerCounter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_webServerOptions.DebugProductionMode) await InvalidateAllNodeStatusAsync(stoppingToken);

        await foreach (var arrayPoolCollection in _hearBeatMessageBatchQueue.ReceiveAllAsync(stoppingToken))
        {
            var stopwatch = new Stopwatch();
            var count = arrayPoolCollection.Count;
            try
            {
                stopwatch.Start();
                await ProcessHeartBeatMessagesAsync(arrayPoolCollection);
                _logger.LogInformation(
                    $"process {arrayPoolCollection.Count} messages,spent: {stopwatch.Elapsed}, AvailableCount:{_hearBeatMessageBatchQueue.AvailableCount}");
                _webServerCounter.HeartBeatAvailableCount = (uint)_hearBeatMessageBatchQueue.AvailableCount;
                _webServerCounter.HeartBeatTotalProcessTimeSpan += stopwatch.Elapsed;
                _webServerCounter.HeartBeatConsumeCount += (uint)count;
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
            finally
            {
                _logger.LogInformation(
                    $"process {count} messages, spent:{stopwatch.Elapsed}, AvailableCount:{_hearBeatMessageBatchQueue.AvailableCount}");
                stopwatch.Reset();
            }
        }
    }

    private async Task InvalidateAllNodeStatusAsync(CancellationToken stoppingToken)
    {
        using var repo = _nodeInfoRepositoryFactory.CreateRepository();
        try
        {
            var nodes = await repo.ListAsync(new NodeInfoSpecification(AreaTags.Any, NodeStatus.Online, []));
            foreach (var item in nodes)
                item.Status = NodeStatus.Offline;
            await repo.UpdateRangeAsync(nodes);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    private async Task ProcessHeartBeatMessagesAsync(
        ArrayPoolCollection<NodeHeartBeatSessionMessage> arrayPoolCollection,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = new Stopwatch();
        try
        {
            using var nodeInfoRepo = _nodeInfoRepositoryFactory.CreateRepository();
            using var nodePropsRepo = _nodePropertyRepositoryFactory.CreateRepository();
            await RefreshNodeSettingsAsync();
            var nodeList = new List<NodeInfoModel>();
            foreach (var hearBeatSessionMessage in arrayPoolCollection)
            {
                if (hearBeatSessionMessage == null) continue;
                stopwatch.Start();
                await ProcessHeartBeatMessageAsync(
                    nodeInfoRepo,
                    nodePropsRepo,
                    hearBeatSessionMessage,
                    nodeList,
                    cancellationToken);
                stopwatch.Stop();
                _logger.LogInformation(
                    $"process heartbeat {hearBeatSessionMessage.NodeSessionId} spent:{stopwatch.Elapsed}");
                stopwatch.Reset();
            }
            stopwatch.Start();
            await nodeInfoRepo.UpdateRangeAsync(nodeList, cancellationToken);
            stopwatch.Stop();
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
        finally
        {
            _logger.LogInformation(
                $"Process {arrayPoolCollection.Count} messages, SaveElapsed:{stopwatch.Elapsed}");
            stopwatch.Reset();
        }
    }

    private async Task RefreshNodeSettingsAsync(CancellationToken cancellationToken = default)
    {
        using var repo = _propertyBagRepositoryFactory.CreateRepository();
        var propertyBag =
            await repo.FirstOrDefaultAsync(new PropertyBagSpecification(nameof(NodeSettings)), cancellationToken);
        if (propertyBag == null || !propertyBag.TryGetValue("Value", out var value))
            _nodeSettings = new NodeSettings();
        else
            _nodeSettings = JsonSerializer.Deserialize<NodeSettings>(value as string);
    }

    private async Task ProcessHeartBeatMessageAsync(
        IRepository<NodeInfoModel> nodeInfoRepo,
        IRepository<NodePropertySnapshotModel> nodePropertyRepo,
        NodeHeartBeatSessionMessage hearBeatMessage,
        List<NodeInfoModel> nodeList,
        CancellationToken cancellationToken = default
    )
    {
        NodeInfoModel? nodeInfo = null;
        try
        {
            var hearBeatResponse = hearBeatMessage.GetMessage();
            if (hearBeatMessage.NodeSessionId.NodeId == NodeId.Null)
            {
                _logger.LogInformation($"invalid node id: {hearBeatResponse.Properties["RemoteIpAddress"]}");
                return;
            }

            nodeInfo = await nodeInfoRepo.GetByIdAsync(hearBeatMessage.NodeSessionId.NodeId.Value);

            if (nodeInfo == null) return;

            var nodeName = _nodeSessionService.GetNodeName(hearBeatMessage.NodeSessionId);
            var nodeStatus = _nodeSessionService.GetNodeStatus(hearBeatMessage.NodeSessionId);
            nodeInfo.Status = nodeStatus;
            var propsDict =
                await _memoryCache.GetOrCreateAsync<ConcurrentDictionary<string, string>>("NodeProps:" + nodeInfo.Id,
                    TimeSpan.FromHours(1));
            nodeList.Add(nodeInfo);
            if (hearBeatResponse != null)
            {
                nodeInfo.Profile.UpdateTime =
                    DateTime.ParseExact(hearBeatResponse.Properties[NodePropertyModel.LastUpdateDateTime_Key],
                        NodePropertyModel.DateTimeFormatString, DateTimeFormatInfo.InvariantInfo);
                nodeInfo.Profile.ServerUpdateTimeUtc = DateTime.UtcNow;
                nodeInfo.Profile.Name = nodeInfo.Name;
                nodeInfo.Profile.NodeInfoId = nodeInfo.Id;
                nodeInfo.Profile.ClientVersion = hearBeatResponse.Properties[NodePropertyModel.ClientVersion_Key];
                nodeInfo.Profile.IpAddress = hearBeatResponse.Properties["RemoteIpAddress"];
                nodeInfo.Profile.InstallStatus = true;
                nodeInfo.Profile.LoginName = hearBeatResponse.Properties[NodePropertyModel.Environment_UserName_Key];
                nodeInfo.Profile.FactoryName = "Unknown";
                if (!string.IsNullOrEmpty(nodeInfo.Profile.IpAddress))
                    foreach (var mapping in _nodeSettings.IpAddressMappings)
                    {
                        if (string.IsNullOrEmpty(mapping.Name)
                            || string.IsNullOrEmpty(mapping.Value))
                            continue;

                        if (nodeInfo.Profile.IpAddress.StartsWith(mapping.Value))
                        {
                            nodeInfo.Profile.FactoryName = mapping.Tag;
                            break;
                        }
                    }


                foreach (var item in hearBeatResponse.Properties)
                {
                    if (item.Key == null) continue;
                    if (!propsDict.TryGetValue(item.Key, out var oldValue))
                        propsDict.TryAdd(item.Key, item.Value);
                    else
                        propsDict.TryUpdate(item.Key, item.Value, oldValue);
                }

                if (propsDict.Count > 0)
                    if (propsDict.TryGetValue(NodePropertyModel.Process_Processes_Key, out var processString))
                        if (!string.IsNullOrEmpty(processString) && processString.Contains('['))
                        {
                            var processInfoList = JsonSerializer.Deserialize<ProcessInfo[]>(processString);
                            AnalysisProcessInfoList(nodeInfo, processInfoList);
                        }
            }


            if (nodeInfo.Status == NodeStatus.Offline)
            {
                var nodeProps =
                    new NodePropertySnapshotModel
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = $"{nodeInfo.Name} Snapshot",
                        CreationDateTime = nodeInfo.Profile.UpdateTime,
                        NodeProperties = propsDict.Select(NodePropertyEntry.From).ToList(),
                        NodeInfoId = nodeInfo.Id
                    };
                await nodePropertyRepo.AddAsync(nodeProps);
                var oldId = nodeInfo.LastNodePropertySnapshotId;
                nodeInfo.LastNodePropertySnapshotId = nodeProps.Id;
                await nodePropertyRepo.DbContext.Set<NodePropertySnapshotModel>().Where(x => x.Id == oldId)
                    .ExecuteDeleteAsync();
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError($"{ex}");
        }
    }

    private void AnalysisProcessInfoList(NodeInfoModel nodeInfo, ProcessInfo[]? processInfoList)
    {
        if (_nodeSettings.ProcessUsagesMapping != null && processInfoList != null)
        {
            var usagesList = nodeInfo.Profile.Usages?.Split(',') ?? [];
            var usages = new HashSet<string>(usagesList);
            foreach (var mapping in _nodeSettings.ProcessUsagesMapping)
            {
                if (string.IsNullOrEmpty(mapping.Name)
                    || string.IsNullOrEmpty(mapping.Value))
                    continue;
                foreach (var processInfo in processInfoList)
                    if (processInfo.FileName.Contains(mapping.Name, StringComparison.OrdinalIgnoreCase))
                        usages.Add(mapping.Value);
            }

            nodeInfo.Profile.Usages = usages.Count == 0 ? null : string.Join(",", usages.OrderBy(static x => x));
        }
    }
}