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
    private readonly ExceptionCounter _exceptionCounter;
    private readonly BatchQueue<NodeHeartBeatSessionMessage> _hearBeatMessageBatchQueue;
    private readonly ILogger<HeartBeatResponseConsumerService> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepositoryFactory;
    private readonly ApplicationRepositoryFactory<NodePropertySnapshotModel> _nodePropertyRepositoryFactory;
    private readonly INodeSessionService _nodeSessionService;
    private readonly ApplicationRepositoryFactory<PropertyBag> _propertyBagRepositoryFactory;
    private readonly ApplicationRepositoryFactory<TaskDefinitionModel> _taskDefinitionRepositoryFactory;
    private readonly WebServerCounter _webServerCounter;
    private readonly IAsyncQueue<NotificationMessage> _notificationQueue;
    private readonly WebServerOptions _webServerOptions;
    private NodeSettings _nodeSettings;

    public HeartBeatResponseConsumerService(
        ExceptionCounter exceptionCounter,
        ILogger<HeartBeatResponseConsumerService> logger,
        INodeSessionService nodeSessionService,
        IAsyncQueue<NotificationMessage> notificationQueue,
        ApplicationRepositoryFactory<TaskDefinitionModel> taskDefinitionRepositoryFactory,
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
        _notificationQueue = notificationQueue;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!_webServerOptions.DebugProductionMode) await InvalidateAllNodeStatusAsync(cancellationToken);

        await foreach (var array in _hearBeatMessageBatchQueue.ReceiveAllAsync(cancellationToken))
        {
            var stopwatch = new Stopwatch();
            var count = array.Length;
            try
            {
                stopwatch.Start();
                await ProcessHeartBeatMessagesAsync(array);
                _logger.LogInformation(
                    $"process {array.Length} messages,spent: {stopwatch.Elapsed}, AvailableCount:{_hearBeatMessageBatchQueue.AvailableCount}");
                _webServerCounter.HeartBeatAvailableCount.Value = _hearBeatMessageBatchQueue.AvailableCount;
                _webServerCounter.HeartBeatTotalProcessTimeSpan.Value += stopwatch.Elapsed;
                _webServerCounter.HeartBeatConsumeCount.Value += (uint)count;
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

    private async Task InvalidateAllNodeStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var nodeRepo = _nodeInfoRepositoryFactory.CreateRepository();
            var nodeList = await nodeRepo.ListAsync(
                new NodeInfoSpecification(
                    AreaTags.Any,
                    NodeStatus.Online,
                    NodeDeviceType.Computer,
                    []),
                cancellationToken);
            foreach (var node in nodeList)
            {
                node.Status = NodeStatus.Offline;
            }
            foreach (var array in nodeList.Chunk(50))
            {
                await nodeRepo.UpdateRangeAsync(array, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    private string GetNodeId(NodeHeartBeatSessionMessage sessionMessage)
    {
        return sessionMessage.NodeSessionId.NodeId.Value;
    }

    private async Task ProcessHeartBeatMessagesAsync(
        NodeHeartBeatSessionMessage[] array,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = new Stopwatch();
        try
        {
            using var nodeInfoRepo = _nodeInfoRepositoryFactory.CreateRepository();
            using var nodePropsRepo = _nodePropertyRepositoryFactory.CreateRepository();
            await RefreshNodeSettingsAsync(cancellationToken);
            var nodeIdList = array.Select(GetNodeId);
            var nodesList = await nodeInfoRepo.ListAsync(
                new NodeInfoSpecification(
                    AreaTags.Any,
                    NodeStatus.All,
                    NodeDeviceType.All,
                    DataFilterCollection<string>.Includes(nodeIdList)),
                cancellationToken);
            foreach (var hearBeatSessionMessage in array)
            {
                NodeInfoModel? nodeInfo = null;
                foreach (var item in nodesList)
                {
                    if (item.Id == hearBeatSessionMessage.NodeSessionId.NodeId.Value)
                    {
                        nodeInfo = item;
                        break;
                    }
                }
                if (nodeInfo == null)
                {
                    continue;
                }
                if (hearBeatSessionMessage == null) continue;
                stopwatch.Start();
                await ProcessHeartBeatMessageAsync(
                    nodePropsRepo,
                    hearBeatSessionMessage,
                    nodeInfo,
                    cancellationToken);
                stopwatch.Stop();
                _logger.LogInformation(
                    $"process heartbeat {hearBeatSessionMessage.NodeSessionId} spent:{stopwatch.Elapsed}");
                stopwatch.Reset();
            }

            stopwatch.Start();
            foreach (var nodeChangedArray in nodesList.Chunk(10))
            {
                await nodeInfoRepo.UpdateRangeAsync(nodeChangedArray, cancellationToken);
            }
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
                $"Process {array.Length} messages, SaveElapsed:{stopwatch.Elapsed}");
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

    async ValueTask ProcessHeartBeatMessageAsync(
        IRepository<NodePropertySnapshotModel> nodePropertyRepo,
        NodeHeartBeatSessionMessage hearBeatMessage,
        NodeInfoModel  nodeInfo,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var hearBeatResponse = hearBeatMessage.GetMessage();
            if (hearBeatMessage.NodeSessionId.NodeId == NodeId.Null)
            {
                _logger.LogInformation($"invalid node id: {hearBeatResponse.Properties["RemoteIpAddress"]}");
                return;
            }

            if (nodeInfo == null) return;

            var nodeName = _nodeSessionService.GetNodeName(hearBeatMessage.NodeSessionId);
            var nodeStatus = _nodeSessionService.GetNodeStatus(hearBeatMessage.NodeSessionId);
            nodeInfo.Status = nodeStatus;
            nodeInfo.ModifiedDateTime = DateTime.UtcNow;
            var propsDict = _memoryCache.GetOrCreate<ConcurrentDictionary<string, string>>("NodeProps:" + nodeInfo.Id,
                    TimeSpan.FromHours(1));
            if (hearBeatResponse != null)
            {
                nodeInfo.Profile.UpdateTime = DateTime.ParseExact(
                    hearBeatResponse.Properties[NodePropertyModel.LastUpdateDateTime_Key],
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
                    _nodeSettings.MatchAreaTag(nodeInfo);

                foreach (var item in hearBeatResponse.Properties)
                {
                    if (item.Key == null) continue;
                    if (!propsDict.TryGetValue(item.Key, out var oldValue))
                        propsDict.TryAdd(item.Key, item.Value);
                    else
                        propsDict.TryUpdate(item.Key, item.Value, oldValue);
                }

                if (propsDict != null && !propsDict.IsEmpty)
                {
                    if (propsDict.TryGetValue(NodePropertyModel.Process_Processes_Key,
                            out var processListJsonString)
                        &&
                        !string.IsNullOrEmpty(processListJsonString)
                        &&
                        processListJsonString.Contains('['))
                    {
                        var processInfoList = JsonSerializer.Deserialize<ProcessInfo[]>(processListJsonString);
                        if (processInfoList != null) AnalysisNodeProcessInfoList(nodeInfo, processInfoList);
                    }
                    if (propsDict.TryGetValue(NodePropertyModel.System_Win32Services_Key,
                            out var win32ServiceListJsonString)
                        &&
                        !string.IsNullOrEmpty(win32ServiceListJsonString)
                        &&
                        win32ServiceListJsonString.Contains('['))
                    {
                        var serviceProcessInfoList = JsonSerializer.Deserialize<ServiceProcessInfo[]>(win32ServiceListJsonString);
                        if (serviceProcessInfoList != null) AnalysisNodeServiceProcessInfoList(nodeInfo, serviceProcessInfoList);
                    }

                }

                AnalysisNodeInfo(nodeInfo);
            }


            if (nodeInfo.Status == NodeStatus.Offline)
            {
                var nodeProps =
                    new NodePropertySnapshotModel
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = $"{nodeInfo.Name} Snapshot",
                        CreationDateTime = nodeInfo.Profile.UpdateTime,
                        NodeProperties = propsDict?.Select(NodePropertyEntry.From).ToList() ?? [],
                        NodeInfoId = nodeInfo.Id
                    };
                await nodePropertyRepo.AddAsync(nodeProps, cancellationToken);
                var oldId = nodeInfo.LastNodePropertySnapshotId;
                nodeInfo.LastNodePropertySnapshotId = nodeProps.Id;
                await nodePropertyRepo.DbContext.Set<NodePropertySnapshotModel>().Where(x => x.Id == oldId)
                    .ExecuteDeleteAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError($"{ex}");
        }
    }

    private void AnalysisNodeInfo(NodeInfoModel nodeInfo)
    {
    }

    private void AnalysisNodeProcessInfoList(NodeInfoModel nodeInfo, ProcessInfo[] processInfoList)
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

    private void AnalysisNodeServiceProcessInfoList(NodeInfoModel nodeInfo, ServiceProcessInfo[] serviceProcessInfoList)
    {
        if (_nodeSettings.ProcessUsagesMapping != null && serviceProcessInfoList != null)
        {
            var usagesList = nodeInfo.Profile.Usages?.Split(',') ?? [];
            var usages = new HashSet<string>(usagesList);
            foreach (var mapping in _nodeSettings.ProcessUsagesMapping)
            {
                if (string.IsNullOrEmpty(mapping.Name)
                    || string.IsNullOrEmpty(mapping.Value))
                    continue;
                foreach (var serviceProcessInfo in serviceProcessInfoList)
                {
                    if (serviceProcessInfo.PathName != null && serviceProcessInfo.PathName.Contains(mapping.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        usages.Add(mapping.Value);
                    }
                    else if (serviceProcessInfo.Name != null && serviceProcessInfo.Name.Contains(mapping.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        usages.Add(mapping.Value);
                    }
                }
            }
            nodeInfo.Profile.Usages = usages.Count == 0 ? null : string.Join(",", usages.OrderBy(static x => x));
        }

    }
}