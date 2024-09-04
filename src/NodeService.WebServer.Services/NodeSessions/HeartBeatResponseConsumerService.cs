using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;
using System.Collections.Immutable;
using System.Globalization;

namespace NodeService.WebServer.Services.NodeSessions;

public class HeartBeatResponseConsumerService : BackgroundService
{
    readonly ExceptionCounter _exceptionCounter;
    readonly BatchQueue<NodeHeartBeatSessionMessage> _hearBeatSessionMessageBatchQueue;
    readonly ILogger<HeartBeatResponseConsumerService> _logger;
    readonly IMemoryCache _memoryCache;
    readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepositoryFactory;
    readonly ApplicationRepositoryFactory<NodePropertySnapshotModel> _nodePropertyRepositoryFactory;
    readonly ApplicationRepositoryFactory<PropertyBag> _propertyBagRepositoryFactory;
    readonly ApplicationRepositoryFactory<TaskDefinitionModel> _taskDefinitionRepositoryFactory;
    readonly INodeSessionService _nodeSessionService;
    readonly WebServerCounter _webServerCounter;
    readonly IAsyncQueue<NotificationMessage> _notificationQueue;
    readonly NodeInfoQueryService _nodeInfoQueryService;
    readonly BatchQueue<NodeStatusChangeRecordModel> _nodeStatusChangeRecordBatchQueue;
    readonly ObjectCache _objectCache;
    readonly ConfigurationQueryService _configurationQueryService;
    readonly WebServerOptions _webServerOptions;
    NodeSettings _nodeSettings;
    IEnumerable<NodeUsageConfigurationModel> _nodeUsageConfigList = [];
    readonly ConcurrentDictionary<string, object?> _nodesDict;

    public HeartBeatResponseConsumerService(
        ILogger<HeartBeatResponseConsumerService> logger,
        ExceptionCounter exceptionCounter,
        ObjectCache objectCache,
        IMemoryCache memoryCache,
        WebServerCounter webServerCounter,
        IOptionsMonitor<WebServerOptions> optionsMonitor,
        INodeSessionService nodeSessionService,
        IAsyncQueue<NotificationMessage> notificationQueue,
        NodeInfoQueryService nodeInfoQueryService,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepositoryFactory,
        ApplicationRepositoryFactory<TaskDefinitionModel> taskDefinitionRepositoryFactory,
        ApplicationRepositoryFactory<PropertyBag> propertyBagRepositoryFactory,
        ApplicationRepositoryFactory<NodePropertySnapshotModel> nodePropertyRepositoryFactory,
        BatchQueue<NodeHeartBeatSessionMessage> heartBeatSessionMessageBatchBlock,
        BatchQueue<NodeStatusChangeRecordModel> nodeStatusChangeRecordBatchQueue,
        ConfigurationQueryService configurationQueryService)
    {
        _logger = logger;
        _nodeSessionService = nodeSessionService;
        _taskDefinitionRepositoryFactory = taskDefinitionRepositoryFactory;
        _hearBeatSessionMessageBatchQueue = heartBeatSessionMessageBatchBlock;
        _propertyBagRepositoryFactory = propertyBagRepositoryFactory;
        _nodePropertyRepositoryFactory = nodePropertyRepositoryFactory;
        _nodeInfoRepositoryFactory = nodeInfoRepositoryFactory;
        _memoryCache = memoryCache;
        _webServerOptions = optionsMonitor.CurrentValue;
        _nodeSettings = new NodeSettings();
        _exceptionCounter = exceptionCounter;
        _webServerCounter = webServerCounter;
        _notificationQueue = notificationQueue;
        _nodeInfoQueryService = nodeInfoQueryService;
        _nodeStatusChangeRecordBatchQueue = nodeStatusChangeRecordBatchQueue;
        _objectCache = objectCache;
        _configurationQueryService = configurationQueryService;
        _nodesDict = new ConcurrentDictionary<string, object?>();
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!_webServerOptions.DebugProductionMode)
        {
            await InvalidateAllNodeStatusAsync(
                NodeStatus.All,
                true,
                cancellationToken);
        }

        await Task.WhenAll(
            InvalidateAllNodeStatusAsync(NodeStatus.Offline, TimeSpan.FromMinutes(1), cancellationToken),
            ConsumeHeartBeatMessageAsync(cancellationToken));
    }

    private async Task ConsumeHeartBeatMessageAsync(CancellationToken cancellationToken = default)
    {
        await foreach (var array in _hearBeatSessionMessageBatchQueue.ReceiveAllAsync(cancellationToken))
        {
            if (array == null)
            {
                continue;
            }
            var processTimeSpan = TimeSpan.Zero;
            var timeStamp = Stopwatch.GetTimestamp();
            var count = array.Length;
            try
            {
                await ProcessHeartBeatMessagesAsync(array, cancellationToken);
                processTimeSpan = Stopwatch.GetElapsedTime(timeStamp);
                _webServerCounter.Snapshot.HeartBeatQueueCount.Value = _hearBeatSessionMessageBatchQueue.QueueCount;
                _webServerCounter.Snapshot.HeartBeatTotalProcessTimeSpan.Value += processTimeSpan;
                _webServerCounter.Snapshot.HeartBeatMessageConsumeCount.Value += (uint)count;
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
            finally
            {
                _logger.LogInformation($"process {count} messages, spent:{processTimeSpan}, QueueCount:{_hearBeatSessionMessageBatchQueue.QueueCount}");
            }
        }
    }

    private async Task InvalidateAllNodeStatusAsync(
        NodeStatus nodeStatus,
        TimeSpan timeSpan,
        CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {

            try
            {
                await Task.Delay(timeSpan, cancellationToken);
                await InvalidateAllNodeStatusAsync(
                    nodeStatus,
                    false,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                _exceptionCounter.AddOrUpdate(ex);
            }
        }
    }

    bool IsNotInNodeInfoDict(NodeInfoModel nodeInfo)
    {
        return !_nodesDict.ContainsKey(nodeInfo.Id);
    }


    async ValueTask InvalidateAllNodeStatusAsync(
        NodeStatus nodeStatus,
        bool removeCache,
        CancellationToken cancellationToken = default)
    {
        ImmutableArray<InvalidateNodeContext> invalidateNodeContexts = [];
        try
        {
            await using var nodeRepo = await _nodeInfoRepositoryFactory.CreateRepositoryAsync(cancellationToken);
            var nodeInfoList = await nodeRepo.ListAsync(
                new NodeInfoSpecification(
                    AreaTags.Any,
                    nodeStatus,
                    NodeDeviceType.Computer),
                cancellationToken);

            invalidateNodeContexts = nodeInfoList.Where(IsNotInNodeInfoDict).Select(x => new InvalidateNodeContext(x, removeCache)).ToImmutableArray();

            if (Debugger.IsAttached)
            {
                foreach (var context in invalidateNodeContexts)
                {
                    await InvalidateNodeAsync(context, cancellationToken);
                }
            }
            else
            {
                await Parallel.ForEachAsync(invalidateNodeContexts, new ParallelOptions()
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = 4
                }, InvalidateNodeAsync);
            }

            if (nodeStatus == NodeStatus.All)
            {
                foreach (var invalidateNodeContext in invalidateNodeContexts)
                {
                    invalidateNodeContext.NodeInfo.Status = NodeStatus.Offline;
                }
            }

            await _nodeInfoQueryService.UpdateNodeInfoListAsync(
                invalidateNodeContexts.Select(static x => x.NodeInfo).Where(IsNotInNodeInfoDict).ToArray(),
                cancellationToken);

        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
        finally
        {

        }
    }

    private async ValueTask InvalidateNodeAsync(
        InvalidateNodeContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            await _nodeInfoQueryService.QueryExtendInfoAsync(context.NodeInfo, cancellationToken);
            await SyncPropertiesFromLimsDbAsync(context.NodeInfo, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
            _exceptionCounter.AddOrUpdate(ex, context.NodeInfo.Id);
        }
    }

    string GetNodeId(NodeHeartBeatSessionMessage sessionMessage)
    {
        return sessionMessage.NodeSessionId.NodeId.Value;
    }

    async ValueTask ProcessHeartBeatMessagesAsync(
        NodeHeartBeatSessionMessage[] array,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = new Stopwatch();
        try
        {

            await RefreshNodeSettingsAsync(cancellationToken);
            await RefreshNodeUsagesConfiguationListAsync(cancellationToken);
            var nodeIdList = array.Select(GetNodeId);

            foreach (var item in nodeIdList)
            {
                _nodesDict.TryAdd(item, null);
            }

            stopwatch.Restart();
            var nodeList = await _nodeInfoQueryService.QueryNodeInfoListAsync(
                nodeIdList,
                false,
                cancellationToken);
            _webServerCounter.Snapshot.HeartBeatQueryNodeInfoListTimeSpan.Value += stopwatch.Elapsed;

            stopwatch.Stop();

            foreach (var hearBeatSessionMessage in array)
            {
                NodeInfoModel? nodeInfo = null;
                foreach (var item in nodeList)
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
                hearBeatSessionMessage.NodeInfo = nodeInfo;
            }

            await Parallel.ForEachAsync(array, new ParallelOptions()
            {
                CancellationToken = cancellationToken,
            }, ProcessHeartBeatMessageAsync);

            stopwatch.Start();
            await SaveNodeUsageConfigurationListAsync(cancellationToken);
            await _nodeInfoQueryService.UpdateNodeInfoListAsync(nodeList, cancellationToken);
            _webServerCounter.Snapshot.HeartBeatUpdateNodeInfoListTimeSpan.Value += stopwatch.Elapsed;
            stopwatch.Stop();
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
        finally
        {
            _nodesDict.Clear();
            _logger.LogInformation(
                $"Process {array.Length} messages, SaveElapsed:{stopwatch.Elapsed}");
            stopwatch.Reset();
        }
    }

    private async ValueTask RefreshNodeSettingsAsync(CancellationToken cancellationToken)
    {
        _nodeSettings = await _configurationQueryService.QueryNodeSettingsAsync(cancellationToken);
        if (_nodeSettings == null)
        {
            _nodeSettings = new NodeSettings();
        }
    }

    async ValueTask SaveNodeUsageConfigurationListAsync(CancellationToken cancellationToken = default)
    {
        await Parallel.ForEachAsync(_nodeUsageConfigList.Where(static x => x.HasChanged).ToArray(), new ParallelOptions()
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = 8
        }, SaveNodeUsageConfigurationAsync);
    }

    async ValueTask SaveNodeUsageConfigurationAsync(
        NodeUsageConfigurationModel nodeUsageConfiguration,
        CancellationToken cancellationToken = default) {
        try
        {
            var queryResult = await _configurationQueryService.QueryConfigurationByIdListAsync<NodeUsageConfigurationModel>([nodeUsageConfiguration.Id], cancellationToken);
            if (!queryResult.HasValue)
            {
                return;
            }
            var nodeUsageConfigFromDb = queryResult.Items.FirstOrDefault();
            if (nodeUsageConfigFromDb == null)
            {
                return;
            }
            if (!nodeUsageConfigFromDb.IsEnabled && !nodeUsageConfiguration.Value.AutoDetect)
            {
                return;
            }
            nodeUsageConfiguration.IsEnabled = nodeUsageConfigFromDb.IsEnabled;
            nodeUsageConfiguration.Name = nodeUsageConfigFromDb.Name;
            nodeUsageConfiguration.FactoryName = nodeUsageConfigFromDb.FactoryName;
            nodeUsageConfiguration.Value = nodeUsageConfiguration.Value with
            {
                AutoDetect = nodeUsageConfigFromDb.Value.AutoDetect,
                DynamicDetect = nodeUsageConfigFromDb.Value.DynamicDetect,
                Nodes = nodeUsageConfigFromDb.Value.Nodes.Union(nodeUsageConfiguration.Value.Nodes).ToList(),
                ServiceProcessDetections = nodeUsageConfigFromDb.Value.ServiceProcessDetections,
            };
            await _configurationQueryService.AddOrUpdateConfigurationAsync(
                nodeUsageConfiguration,
            true,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex, nodeUsageConfiguration.Id);
            _logger.LogError(ex.ToString());
        }
    }

    async ValueTask RefreshNodeUsagesConfiguationListAsync(CancellationToken cancellationToken = default)
    {
        var nodeUsageConfigQueryResult = await _configurationQueryService.QueryConfigurationByQueryParametersAsync<NodeUsageConfigurationModel>(new PaginationQueryParameters()
        {
            PageIndex = 1,
            PageSize = int.MaxValue - 1,
            QueryStrategy = QueryStrategy.CachePreferred
        }, cancellationToken);
        if (nodeUsageConfigQueryResult.HasValue)
        {
            _nodeUsageConfigList = nodeUsageConfigQueryResult.Items;
        }
        else
        {
            _nodeUsageConfigList = [];
        }
    }

    async ValueTask ProcessHeartBeatMessageAsync(
        NodeHeartBeatSessionMessage hearBeatMessage,
        CancellationToken cancellationToken = default
    )
    {
        var timeStamp = Stopwatch.GetTimestamp();
        try
        {
            var nodeInfo = hearBeatMessage.NodeInfo;
            var stopwatch = new Stopwatch();
            if (nodeInfo == null) return;
            var heartBeatResponse = hearBeatMessage.GetMessage();
            var nodeName = _nodeSessionService.GetNodeName(hearBeatMessage.NodeSessionId);
            var nodeStatus = _nodeSessionService.GetNodeStatus(hearBeatMessage.NodeSessionId);
            nodeInfo.Status = nodeStatus;

            stopwatch.Start();
            var key = $"{nameof(HeartBeatResponseConsumerService)}/NodePropertySnapshot:{nodeInfo.Id}";
            if (!_memoryCache.TryGetValue<NodePropertySnapshotModel>(key, out var nodePropSnapshot) || nodePropSnapshot == null)
            {
                nodePropSnapshot = await _nodeInfoQueryService.QueryNodePropsAsync(nodeInfo.Id, true, cancellationToken);
                _memoryCache.Set(key, nodePropSnapshot);
            }


            stopwatch.Stop();

            _webServerCounter.Snapshot.HeartBeatQueryNodePropsTimeSpan.Value += stopwatch.Elapsed;

            stopwatch.Restart();

            AnalysisPropsResult analysisPropsResult = default;
            var analysisPropsResultKey = $"NodePropsAnalysisResult:{nodeInfo.Id}";

            if (heartBeatResponse != null)
            {
                if (hearBeatMessage.NodeSessionId.NodeId == NodeId.Null)
                {
                    _logger.LogInformation($"invalid node id: {heartBeatResponse.Properties["RemoteIpAddress"]}");
                    return;
                }
                var propsList = heartBeatResponse.Properties.Select(NodePropertyEntry.From).ToList();
                if (nodePropSnapshot == null)
                {
                    nodePropSnapshot = new NodePropertySnapshotModel
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = $"{nodeInfo.Name}",
                        CreationDateTime = nodeInfo.Profile.UpdateTime,
                        ModifiedDateTime = DateTime.UtcNow,
                        NodeProperties = propsList,
                        NodeInfoId = nodeInfo.Id
                    };

                    await _nodeInfoQueryService.SaveNodePropSnapshotAsync(nodeInfo,
                                                                          nodePropSnapshot,
                                                                          true,
                                                                          cancellationToken);
                    nodeInfo.LastNodePropertySnapshotId = nodePropSnapshot.Id;
                    _memoryCache.Set(key, nodePropSnapshot);
                }
                else
                {
                    nodePropSnapshot.NodeProperties = propsList;
                    await _nodeInfoQueryService.SaveNodePropSnapshotAsync(
                        nodeInfo,
                        nodePropSnapshot,
                        false,
                        cancellationToken);
                }
                nodeInfo.Name = hearBeatMessage.HostName;
                nodeInfo.Profile.UpdateTime = DateTime.ParseExact(
                    heartBeatResponse.Properties[NodePropertyModel.LastUpdateDateTime_Key],
                    NodePropertyModel.DateTimeFormatString, DateTimeFormatInfo.InvariantInfo);
                nodeInfo.Profile.ServerUpdateTimeUtc = hearBeatMessage.UtcRecieveDateTime;
                nodeInfo.Profile.Name = nodeInfo.Name;
                nodeInfo.Profile.NodeInfoId = nodeInfo.Id;
                nodeInfo.Profile.ClientVersion = heartBeatResponse.Properties[NodePropertyModel.ClientVersion_Key];
                nodeInfo.Profile.IpAddress = heartBeatResponse.Properties["RemoteIpAddress"];
                nodeInfo.Profile.InstallStatus = true;
                nodeInfo.Profile.LoginName = heartBeatResponse.Properties[NodePropertyModel.Environment_UserName_Key];
                if (heartBeatResponse.Properties.TryGetValue(NodePropertyModel.Domain_ComputerDomain_Key, out string computerDomain))
                {
                    nodeInfo.Profile.ComputerDomain = computerDomain;
                }
                nodeInfo.Profile.FactoryName = "Unknown";

                await TryUpdateExtendInfo(
                    nodeInfo,
                    heartBeatResponse,
                    cancellationToken);

                await SyncPropertiesFromLimsDbAsync(nodeInfo, cancellationToken);

                if (!string.IsNullOrEmpty(nodeInfo.Profile.IpAddress))
                {
                    _nodeSettings.MatchAreaTag(nodeInfo);
                }

                analysisPropsResult = ProcessProps(nodeInfo, heartBeatResponse);
                List<string> usageList = [];
                if (analysisPropsResult.ServiceProcessListResult.Usages != null)
                {
                    usageList.AddRange(analysisPropsResult.ServiceProcessListResult.Usages);
                }
                if (analysisPropsResult.ProcessListResult.Usages != null)
                {
                    usageList.AddRange(analysisPropsResult.ProcessListResult.Usages);
                }
                nodeInfo.Profile.Usages = string.Join<string>(",", usageList.Distinct());
                await _objectCache.SetObjectAsync(analysisPropsResultKey, analysisPropsResult, cancellationToken);
            }
            if (nodeStatus == NodeStatus.Offline)
            {
                var statusChanged = new NodeStatusChangeRecordModel()
                {
                    Id = Guid.NewGuid().ToString(),
                    CreationDateTime = DateTime.UtcNow,
                    NodeId = hearBeatMessage.NodeSessionId.NodeId.Value,
                    Status = NodeStatus.Offline,
                    IpAddress = hearBeatMessage.IpAddress,
                    Message = $"Offline",
                };
                if (analysisPropsResult == default)
                {
                    analysisPropsResult = await _objectCache.GetObjectAsync<AnalysisPropsResult>(analysisPropsResultKey, cancellationToken);
                }
                if (analysisPropsResult != default)
                {
                    if (analysisPropsResult.ProcessListResult.StatusChangeProcessList != null)
                    {
                        foreach (var item in analysisPropsResult.ProcessListResult.StatusChangeProcessList)
                        {
                            statusChanged.ProcessList.Add(new StringEntry()
                            {
                                Name = item.ProcessName,
                                Value = item.Id.ToString()
                            });
                        }
                    }

                    if (analysisPropsResult.ServiceProcessListResult.StatusChangeProcessList != null)
                    {
                        foreach (var item in analysisPropsResult.ServiceProcessListResult.StatusChangeProcessList)
                        {
                            statusChanged.ProcessList.Add(new StringEntry()
                            {
                                Name = item.Name,
                                Value = item.ProcessId.ToString()
                            });
                        }
                    }

                }

                if (nodePropSnapshot != null)
                {
                    await _nodeInfoQueryService.SaveNodePropSnapshotAsync(
                            nodeInfo,
                            nodePropSnapshot,
                            true,
                            cancellationToken);
                }
                await _nodeStatusChangeRecordBatchQueue.SendAsync(statusChanged, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError($"{ex}");
        }
        finally
        {
            var ellaspedTime = Stopwatch.GetElapsedTime(timeStamp);

        }
    }

    async ValueTask SyncPropertiesFromLimsDbAsync(
        NodeInfoModel nodeInfo,
        CancellationToken cancellationToken = default)
    {
        var computerInfo = await _nodeInfoQueryService.Query_dl_equipment_ctrl_computer_Async(
            nodeInfo.Id,
            nodeInfo.Profile.LimsDataId,
            cancellationToken);

        if (computerInfo == null)
        {
            nodeInfo.Profile.FoundInLims = false;
        }
        else
        {
            nodeInfo.Profile.FoundInLims = true;
            nodeInfo.Profile.LimsDataId = computerInfo.id;
            nodeInfo.Profile.LabArea = computerInfo.LabArea?.name;
            nodeInfo.Profile.LabName = computerInfo.LabInfo?.name;
            nodeInfo.Profile.Manager = computerInfo.manager_name;
            nodeInfo.Profile.Remarks = computerInfo.remark;
        }
    }

    private async ValueTask TryUpdateExtendInfo(NodeInfoModel nodeInfo, HeartBeatResponse heartBeatResponse, CancellationToken cancellationToken)
    {
        try
        {
            var nodeExtendInfo = await _nodeInfoQueryService.QueryExtendInfoAsync(nodeInfo, cancellationToken);
            if (nodeExtendInfo != null)
            {
                if (heartBeatResponse.Properties.TryGetValue(NodePropertyModel.Device_Cpu_SerialNumbers_Key, out string cpuSerialNumbersString))
                {
                    if (cpuSerialNumbersString.Contains('['))
                    {
                        nodeExtendInfo.Value.CpuInfoList = JsonSerializer.Deserialize<List<CpuInfo>>(cpuSerialNumbersString);
                    }
                }

                if (heartBeatResponse.Properties.TryGetValue(NodePropertyModel.Device_BIOS_SerialNumbers_Key, out string biosSerialNumbersString))
                {
                    if (biosSerialNumbersString.Contains('['))
                    {
                        nodeExtendInfo.Value.BIOSInfoList = JsonSerializer.Deserialize<List<BIOSInfo>>(biosSerialNumbersString);
                    }
                }

                if (heartBeatResponse.Properties.TryGetValue(NodePropertyModel.Device_PhysicalMedia_SerialNumbers_Key, out string physicalMediaSerialNumbers))
                {
                    if (biosSerialNumbersString.Contains('['))
                    {
                        nodeExtendInfo.Value.PhysicalMediaInfoList = JsonSerializer.Deserialize<List<PhysicalMediaInfo>>(physicalMediaSerialNumbers);
                    }
                }
                await _nodeInfoQueryService.UpdateExtendInfoAsync(nodeExtendInfo, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
            _exceptionCounter.AddOrUpdate(ex, nodeInfo.Id);
        }

    }

    private AnalysisPropsResult ProcessProps(NodeInfoModel nodeInfo, HeartBeatResponse hearBeatResponse)
    {
        AnalysisPropsResult analysisPropsResult = default;
        try
        {
            if (hearBeatResponse.Properties.Count > 0)
            {
                if (hearBeatResponse.Properties.TryGetValue(NodePropertyModel.Process_Processes_Key,
                        out var processListJsonString)
                    &&
                    !string.IsNullOrEmpty(processListJsonString)
                    &&
                    processListJsonString.Contains('['))
                {
                    var processInfoList = JsonSerializer.Deserialize<ProcessInfo[]>(processListJsonString);
                    if (processInfoList != null)
                    {
                        analysisPropsResult.ProcessListResult = AnalysisNodeProcessInfoList(nodeInfo, processInfoList);
                        analysisPropsResult.ProcessInfoList = processInfoList;
                    }

                }
                if (hearBeatResponse.Properties.TryGetValue(NodePropertyModel.System_Win32Services_Key,
                        out var win32ServiceListJsonString)
                    &&
                    !string.IsNullOrEmpty(win32ServiceListJsonString)
                    &&
                    win32ServiceListJsonString.Contains('['))
                {
                    var serviceProcessInfoList = JsonSerializer.Deserialize<ServiceProcessInfo[]>(win32ServiceListJsonString);
                    if (serviceProcessInfoList != null)
                    {
                        analysisPropsResult.ServiceProcessListResult = AnalysisNodeServiceProcessInfoList(nodeInfo, serviceProcessInfoList);
                        analysisPropsResult.ServiceProcessInfoList = serviceProcessInfoList;
                    }
                }
            }

        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }

        return analysisPropsResult;
    }

    private AnalysisProcessListResult AnalysisNodeProcessInfoList(NodeInfoModel nodeInfo, ProcessInfo[] processInfoList)
    {
        var analysisProcessListResult = new AnalysisProcessListResult();
        try
        {
            if (_nodeSettings.ProcessUsagesMapping != null && processInfoList != null)
            {
                var usagesList = nodeInfo.Profile.Usages?.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
                var usages = new HashSet<string>(usagesList);
                foreach (var nodeUsageConfiguration in _nodeUsageConfigList)
                {
                    if (!nodeUsageConfiguration.IsEnabled)
                    {
                        continue;
                    }
                    if (!nodeUsageConfiguration.AutoDetect)
                    {
                        continue;
                    }
                    if (nodeUsageConfiguration.FactoryName != AreaTags.Any && nodeUsageConfiguration.FactoryName != nodeInfo.Profile.FactoryName)
                    {
                        continue;
                    }
                    var detectedCount = 0;
                    foreach (var processInfo in processInfoList)
                    {
                        var isDetected = nodeUsageConfiguration.DetectProcess(processInfo);
                        if (isDetected)
                        {
                            detectedCount++;
                            usages.Add(nodeUsageConfiguration.Name);
                        }
                    }
                    if (detectedCount > 0)
                    {
                        nodeUsageConfiguration.AddToConfigurationNodeList(nodeInfo);
                    }
                    else if (detectedCount == 0 && nodeUsageConfiguration.Value.DynamicDetect)
                    {
                        nodeUsageConfiguration.Value.Nodes.RemoveAll(x => x.NodeInfoId == nodeInfo.Id);
                        nodeUsageConfiguration.Value.Nodes = [.. nodeUsageConfiguration.Value.Nodes];
                        nodeUsageConfiguration.HasChanged = true;
                    }
                }
                analysisProcessListResult.Usages = usages;
            }
            if (_nodeSettings.NodeStatusChangeRememberProcessList != null && processInfoList != null)
            {
                foreach (var item in _nodeSettings.NodeStatusChangeRememberProcessList)
                {
                    if (string.IsNullOrEmpty(item.Name)
                        || string.IsNullOrEmpty(item.Value))
                    {
                        continue;
                    }
                    foreach (var processInfo in processInfoList)
                    {
                        if (processInfo.FileName.Contains(item.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            analysisProcessListResult.StatusChangeProcessList.Add(processInfo);
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

        return analysisProcessListResult;
    }

    private AnalysisServiceProcessListResult AnalysisNodeServiceProcessInfoList(NodeInfoModel nodeInfo, ServiceProcessInfo[] serviceProcessInfoList)
    {
        var analisisProcessListResult = new AnalysisServiceProcessListResult();
        try
        {
            if (_nodeSettings.ProcessUsagesMapping != null && serviceProcessInfoList != null)
            {
                var usagesList = nodeInfo.Profile.Usages?.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
                var usages = new HashSet<string>(usagesList);
                foreach (var nodeUsageConfiguration in _nodeUsageConfigList)
                {
                    if (!nodeUsageConfiguration.IsEnabled)
                    {
                        continue;
                    }
                    if (!nodeUsageConfiguration.AutoDetect)
                    {
                        continue;
                    }
                    if (nodeUsageConfiguration.FactoryName != AreaTags.Any && nodeUsageConfiguration.FactoryName != nodeInfo.Profile.FactoryName)
                    {
                        continue;
                    }
                    var detectedCount = 0;
                    foreach (var serviceProcessInfo in serviceProcessInfoList)
                    {
                        var isDetected = nodeUsageConfiguration.DetectServiceProcess(serviceProcessInfo);
                        if (isDetected)
                        {
                            detectedCount++;
                            usages.Add(nodeUsageConfiguration.Name);
                            nodeUsageConfiguration.AddToConfigurationNodeList(nodeInfo);
                        }
                    }
                    if (detectedCount > 0)
                    {
                        nodeUsageConfiguration.AddToConfigurationNodeList(nodeInfo);
                    }
                    else if (detectedCount == 0 && nodeUsageConfiguration.Value.DynamicDetect)
                    {
                        nodeUsageConfiguration.Value.Nodes.RemoveAll(x => x.NodeInfoId == nodeInfo.Id);
                        nodeUsageConfiguration.Value.Nodes = [.. nodeUsageConfiguration.Value.Nodes];
                        nodeUsageConfiguration.HasChanged = true;
                    }
                }
                analisisProcessListResult.Usages = usages;

            }
            if (_nodeSettings.NodeStatusChangeRememberProcessList != null && serviceProcessInfoList != null)
            {
                foreach (var item in _nodeSettings.NodeStatusChangeRememberProcessList)
                {
                    foreach (var processInfo in serviceProcessInfoList)
                    {
                        if (processInfo.Name.Contains(item.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            analisisProcessListResult.StatusChangeProcessList.Add(processInfo);
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

        return analisisProcessListResult;
    }


}