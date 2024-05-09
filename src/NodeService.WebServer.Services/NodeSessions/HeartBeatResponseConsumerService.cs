using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NodeService.Infrastructure.Concurrent;
using NodeService.WebServer.Data;
using NodeService.WebServer.Models;

namespace NodeService.WebServer.Services.NodeSessions;

public class HeartBeatResponseConsumerService : BackgroundService
{
    readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    readonly BatchQueue<NodeHeartBeatSessionMessage> _hearBeatMessageBatchQueue;
    readonly ILogger<HeartBeatResponseConsumerService> _logger;
    readonly IMemoryCache _memoryCache;
    readonly INodeSessionService _nodeSessionService;
    readonly WebServerOptions _webServerOptions;
    NodeSettings _nodeSettings;
    readonly ExceptionCounter _exceptionCounter;
    readonly WebServerCounter _webServerCounter;

    public HeartBeatResponseConsumerService(
        ExceptionCounter exceptionCounter,
        ILogger<HeartBeatResponseConsumerService> logger,
        INodeSessionService nodeSessionService,
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        BatchQueue<NodeHeartBeatSessionMessage> heartBeatMessageBatchBlock,
        IMemoryCache memoryCache,
        IOptionsMonitor<WebServerOptions> optionsMonitor,
        WebServerCounter webServerCounter
    )
    {
        _logger = logger;
        _nodeSessionService = nodeSessionService;
        _dbContextFactory = dbContextFactory;
        _hearBeatMessageBatchQueue = heartBeatMessageBatchBlock;
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
            var count = 0;
            try
            {
                count = arrayPoolCollection.CountNotNull();
                if (count == 0) continue;

                stopwatch.Start();
                await ProcessHeartBeatMessagesAsync(arrayPoolCollection);
                _logger.LogInformation(
                    $"process {arrayPoolCollection.CountNotNull()} messages,spent: {stopwatch.Elapsed}, AvailableCount:{_hearBeatMessageBatchQueue.AvailableCount}");
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
                arrayPoolCollection.Dispose();
            }
        }
    }

    async Task InvalidateAllNodeStatusAsync(CancellationToken stoppingToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        try
        {
            foreach (var item in dbContext.NodeInfoDbSet.AsQueryable().Where(x => x.Status == NodeStatus.Online))
                item.Status = NodeStatus.Offline;
            await dbContext.SaveChangesAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    async Task ProcessHeartBeatMessagesAsync(
        ArrayPoolCollection<NodeHeartBeatSessionMessage> arrayPoolCollection)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var stopwatch = new Stopwatch();
        try
        {
            await RefreshNodeSettings(dbContext);
            foreach (var hearBeatSessionMessage in arrayPoolCollection)
            {
                if (hearBeatSessionMessage == null) continue;
                stopwatch.Start();
                await ProcessHeartBeatMessageAsync(dbContext, hearBeatSessionMessage);
                stopwatch.Stop();
                _logger.LogInformation(
                    $"process heartbeat {hearBeatSessionMessage.NodeSessionId} spent:{stopwatch.Elapsed}");
                stopwatch.Reset();
            }

            stopwatch.Start();
            await dbContext.SaveChangesAsync();
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
                $"Process {arrayPoolCollection.CountNotNull()} messages, SaveElapsed:{stopwatch.Elapsed}");
            stopwatch.Reset();
        }
    }

    async Task RefreshNodeSettings(ApplicationDbContext dbContext)
    {
        var dict = await dbContext.PropertyBagDbSet.FindAsync("NodeSettings");
        if (dict == null || !dict.TryGetValue("Value", out var value))
            _nodeSettings = new NodeSettings();
        else
            _nodeSettings = JsonSerializer.Deserialize<NodeSettings>(value as string);
    }

    async Task ProcessHeartBeatMessageAsync(
        ApplicationDbContext dbContext,
        NodeHeartBeatSessionMessage hearBeatMessage,
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

            nodeInfo = await dbContext.FindAsync<NodeInfoModel>(hearBeatMessage.NodeSessionId.NodeId.Value);

            if (nodeInfo == null) return;

            var nodeName = _nodeSessionService.GetNodeName(hearBeatMessage.NodeSessionId);
            var nodeStatus = _nodeSessionService.GetNodeStatus(hearBeatMessage.NodeSessionId);
            nodeInfo.Status = nodeStatus;
            var propsDict =
                await _memoryCache.GetOrCreateAsync<ConcurrentDictionary<string, string>>("NodeProps:" + nodeInfo.Id,
                    TimeSpan.FromHours(1));

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
                var nodePropertySnapshotModel =
                    new NodePropertySnapshotModel
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = $"{nodeInfo.Name} Snapshot",
                        CreationDateTime = nodeInfo.Profile.UpdateTime,
                        NodeProperties = propsDict.Select(NodePropertyEntry.From).ToList(),
                        NodeInfoId = nodeInfo.Id
                    };
                await dbContext.NodePropertiesSnapshotsDbSet.AddAsync(nodePropertySnapshotModel);
                var oldId = nodeInfo.LastNodePropertySnapshotId;
                nodeInfo.LastNodePropertySnapshotId = nodePropertySnapshotModel.Id;
                await dbContext.NodePropertiesSnapshotsDbSet.Where(x => x.Id == oldId).ExecuteDeleteAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError($"{ex}");
        }
    }

    void AnalysisProcessInfoList(NodeInfoModel nodeInfo, ProcessInfo[]? processInfoList)
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