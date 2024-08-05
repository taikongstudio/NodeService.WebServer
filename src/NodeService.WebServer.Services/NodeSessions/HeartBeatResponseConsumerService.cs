using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;
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
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!_webServerOptions.DebugProductionMode) await InvalidateAllNodeStatusAsync(cancellationToken);

        await foreach (var array in _hearBeatSessionMessageBatchQueue.ReceiveAllAsync(cancellationToken))
        {
            var stopwatch = new Stopwatch();
            var count = array.Length;
            try
            {
                stopwatch.Start();
                await ProcessHeartBeatMessagesAsync(array, cancellationToken);
                _webServerCounter.HeartBeatQueueCount.Value = _hearBeatSessionMessageBatchQueue.QueueCount;
                _webServerCounter.HeartBeatTotalProcessTimeSpan.Value += stopwatch.Elapsed;
                _webServerCounter.HeartBeatMessageConsumeCount.Value += (uint)count;
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
            finally
            {
                _logger.LogInformation(
                    $"process {count} messages, spent:{stopwatch.Elapsed}, QueueCount:{_hearBeatSessionMessageBatchQueue.QueueCount}");
                stopwatch.Reset();
            }
        }
    }

    private async Task InvalidateAllNodeStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var nodeRepo = await _nodeInfoRepositoryFactory.CreateRepositoryAsync();
            var nodeList = await nodeRepo.ListAsync(
                new NodeInfoSpecification(
                    AreaTags.Any,
                    NodeStatus.Online,
                    NodeDeviceType.All),
                cancellationToken);
            foreach (var node in nodeList)
            {
                node.Status = NodeStatus.Offline;
            }
            foreach (var array in nodeList.Chunk(40))
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
            stopwatch.Restart();
            var nodeList = await _nodeInfoQueryService.QueryNodeInfoListAsync(
                nodeIdList,
                false,
                cancellationToken);
            _webServerCounter.HeartBeatQueryNodeInfoListTimeSpan.Value += stopwatch.Elapsed;

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
                MaxDegreeOfParallelism = 4
            }, ProcessHeartBeatMessageAsync);

            stopwatch.Start();
            await SaveNodeUsageConfigurationAsync(cancellationToken);
            await _nodeInfoQueryService.UpdateNodeInfoListAsync(nodeList, cancellationToken);
            _webServerCounter.HeartBeatUpdateNodeInfoListTimeSpan.Value += stopwatch.Elapsed;
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

    async ValueTask SaveNodeUsageConfigurationAsync(CancellationToken cancellationToken = default)
    {
        foreach (var item in _nodeUsageConfigList)
        {
            try
            {
                item.Value = item.Value with { };
                await _configurationQueryService.AddOrUpdateConfigurationAsync(
                    item,
                    true,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex, item.Id);
                _logger.LogError(ex.ToString());
            }
        }
    }

    async ValueTask RefreshNodeSettingsAsync(CancellationToken cancellationToken = default)
    {
       await using var repo =await _propertyBagRepositoryFactory.CreateRepositoryAsync(cancellationToken);
        var propertyBag =
            await repo.FirstOrDefaultAsync(new PropertyBagSpecification(nameof(NodeSettings)), cancellationToken);
        if (propertyBag == null || !propertyBag.TryGetValue("Value", out var value))
            _nodeSettings = new NodeSettings();
        else
            _nodeSettings = JsonSerializer.Deserialize<NodeSettings>(value as string);
    }

    async ValueTask RefreshNodeUsagesConfiguationListAsync(CancellationToken cancellationToken = default)
    {
        var nodeUsageConfigQueryResult = await _configurationQueryService.QueryConfigurationByQueryParametersAsync<NodeUsageConfigurationModel>(new PaginationQueryParameters()
        {
            PageIndex = 1,
            PageSize = int.MaxValue - 1,
            QueryStrategy = QueryStrategy.QueryPreferred
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
            var hearBeatResponse = hearBeatMessage.GetMessage();
            var nodeName = _nodeSessionService.GetNodeName(hearBeatMessage.NodeSessionId);
            var nodeStatus = _nodeSessionService.GetNodeStatus(hearBeatMessage.NodeSessionId);
            nodeInfo.Status = nodeStatus;

            stopwatch.Start();

            var nodePropSnapshot = await _nodeInfoQueryService.QueryNodePropsAsync(nodeInfo.Id, false, cancellationToken);

            stopwatch.Stop();

            _webServerCounter.HeartBeatQueryNodePropsTimeSpan.Value += stopwatch.Elapsed;

            stopwatch.Restart();

            AnalysisPropsResult analysisPropsResult = default;
            var analysisPropsResultKey = $"NodePropsAnalysisResult:{nodeInfo.Id}";

            if (hearBeatResponse != null)
            {
                if (hearBeatMessage.NodeSessionId.NodeId == NodeId.Null)
                {
                    _logger.LogInformation($"invalid node id: {hearBeatResponse.Properties["RemoteIpAddress"]}");
                    return;
                }
                var propsList = hearBeatResponse.Properties.Select(NodePropertyEntry.From).ToList();
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
                    hearBeatResponse.Properties[NodePropertyModel.LastUpdateDateTime_Key],
                    NodePropertyModel.DateTimeFormatString, DateTimeFormatInfo.InvariantInfo);
                nodeInfo.Profile.ServerUpdateTimeUtc = DateTime.UtcNow;
                nodeInfo.Profile.Name = nodeInfo.Name;
                nodeInfo.Profile.NodeInfoId = nodeInfo.Id;
                nodeInfo.Profile.ClientVersion = hearBeatResponse.Properties[NodePropertyModel.ClientVersion_Key];
                nodeInfo.Profile.IpAddress = hearBeatResponse.Properties["RemoteIpAddress"];
                nodeInfo.Profile.InstallStatus = true;
                nodeInfo.Profile.LoginName = hearBeatResponse.Properties[NodePropertyModel.Environment_UserName_Key];
                if (hearBeatResponse.Properties.TryGetValue(NodePropertyModel.Domain_ComputerDomain_Key, out string computerDomain))
                {
                    nodeInfo.Profile.ComputerDomain = computerDomain;
                }
                nodeInfo.Profile.FactoryName = "Unknown";

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
                    if (nodeInfo.Profile.LimsDataId == null)
                    {
                        nodeInfo.Profile.LimsDataId = computerInfo.id;
                    }
                    nodeInfo.Profile.FoundInLims = true;
                    nodeInfo.Profile.LabArea = computerInfo.LabArea?.name;
                    nodeInfo.Profile.LabName = computerInfo.LabInfo?.name;
                    nodeInfo.Profile.Manager = computerInfo.manager_name;
                }


                if (!string.IsNullOrEmpty(nodeInfo.Profile.IpAddress))
                {
                    _nodeSettings.MatchAreaTag(nodeInfo);
                }

                analysisPropsResult = ProcessProps(nodeInfo, hearBeatResponse);
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
                var usagesList = nodeInfo.Profile.Usages?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
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
                    var detectedCount = 0;
                    foreach (var processInfo in processInfoList)
                    {
                        var isDetected = DetectProcess(nodeUsageConfiguration, processInfo);
                        if (isDetected)
                        {
                            detectedCount++;
                            usages.Add(nodeUsageConfiguration.Name);
                        }
                    }
                    if (detectedCount > 0)
                    {
                        AddToConfigurationNodeList(nodeInfo, nodeUsageConfiguration);
                    }
                    else if (detectedCount == 0 && nodeUsageConfiguration.Value.DynamicDetect)
                    {
                        nodeUsageConfiguration.Value.Nodes.RemoveAll(x => x.NodeInfoId == nodeInfo.Id);
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

    private static bool DetectProcess(NodeUsageConfigurationModel nodeUsageConfiguration, ProcessInfo processInfo)
    {
        var isDetected = false;
        foreach (var detection in nodeUsageConfiguration.ServiceProcessDetections)
        {
            switch (detection.DetectionType)
            {
                case NodeUsageServiceProcessDetectionType.FileName:
                    if (processInfo.FileName.Contains(detection.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        isDetected = true;
                    }
                    break;
                case NodeUsageServiceProcessDetectionType.ProcessName:
                    if (processInfo.FileName.Contains(detection.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        isDetected = true;
                    }
                    break;
                case NodeUsageServiceProcessDetectionType.ServiceName:
                    break;
                default:
                    break;
            }
        }

        return isDetected;
    }

    private AnalysisServiceProcessListResult AnalysisNodeServiceProcessInfoList(NodeInfoModel nodeInfo, ServiceProcessInfo[] serviceProcessInfoList)
    {
        var analisisProcessListResult = new AnalysisServiceProcessListResult();
        try
        {
            if (_nodeSettings.ProcessUsagesMapping != null && serviceProcessInfoList != null)
            {
                var usagesList = nodeInfo.Profile.Usages?.Split(',') ?? [];
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
                        var isDetected = DetectServiceProcess(nodeUsageConfiguration, serviceProcessInfo);
                        if (isDetected)
                        {
                            detectedCount++;
                            usages.Add(nodeUsageConfiguration.Name);
                            AddToConfigurationNodeList(nodeInfo, nodeUsageConfiguration);
                        }
                    }
                    if (detectedCount > 0)
                    {
                        AddToConfigurationNodeList(nodeInfo, nodeUsageConfiguration);
                    }
                    else if (detectedCount == 0 && nodeUsageConfiguration.Value.DynamicDetect)
                    {
                        nodeUsageConfiguration.Value.Nodes.RemoveAll(x => x.NodeInfoId == nodeInfo.Id);
                        nodeUsageConfiguration.Value.Nodes = [.. nodeUsageConfiguration.Value.Nodes];
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

    private static bool DetectServiceProcess(NodeUsageConfigurationModel nodeUsageConfiguration, ServiceProcessInfo serviceProcessInfo)
    {
        var isDetected = false;
        foreach (var detection in nodeUsageConfiguration.ServiceProcessDetections)
        {
            switch (detection.DetectionType)
            {
                case NodeUsageServiceProcessDetectionType.FileName:
                    if (serviceProcessInfo.PathName.Contains(detection.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        isDetected = true;
                    }
                    break;
                case NodeUsageServiceProcessDetectionType.ProcessName:

                    break;
                case NodeUsageServiceProcessDetectionType.ServiceName:
                    if (serviceProcessInfo.Name.Contains(detection.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        isDetected = true;
                    }
                    break;
                default:
                    break;
            }
        }

        return isDetected;
    }

    private static void AddToConfigurationNodeList(NodeInfoModel nodeInfo, NodeUsageConfigurationModel nodeUsageConfiguration)
    {
        if (nodeInfo == null)
        {
            return;
        }
        if (nodeUsageConfiguration == null)
        {
            return;
        }
        if (nodeUsageConfiguration.Nodes == null)
        {
            return;
        }
        lock (nodeUsageConfiguration.Nodes)
        {
            NodeUsageInfo? nodeUsageInfo = null;
            foreach (var item in nodeUsageConfiguration.Nodes)
            {
                if (item.NodeInfoId == nodeInfo.Id)
                {
                    nodeUsageInfo = item;
                    break;
                }
            }
            if (nodeUsageInfo == null)
            {
                nodeUsageInfo = new NodeUsageInfo()
                {
                    Name = nodeInfo.Name,
                    NodeInfoId = nodeInfo.Id
                };
                nodeUsageConfiguration.Nodes.Add(nodeUsageInfo);
            }
        }
    }
}