using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Messages;
using NodeService.Infrastructure.Models;
using NodeService.WebServer.Data;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.NodeSessions;
using RocksDbSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services
{
    public class HeartBeatResponseConsumerService : BackgroundService
    {
        private readonly INodeSessionService _nodeSessionService;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly IMemoryCache _memoryCache;
        private readonly WebServerOptions _webServerOptions;
        private readonly ILogger<HeartBeatResponseConsumerService> _logger;
        private readonly RocksDatabase _rocksDatabase;
        private readonly IDisposable? _processUsageAnalysisMonitorToken;
        private ProcessUsageAnalysis _processUsageAnalysis;
        private readonly Infrastructure.Models.RocksDatabase _rocksDb;
        private readonly BatchQueue<NodeHeartBeatSessionMessage> _hearBeatMessageBatchQueue;

        public HeartBeatResponseConsumerService(
            ILogger<HeartBeatResponseConsumerService> logger,
            INodeSessionService nodeSessionService,
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            BatchQueue<NodeHeartBeatSessionMessage> heartBeatMessageBatchBlock,
            IMemoryCache memoryCache,
            IOptionsMonitor<WebServerOptions> optionsMonitor,
            IOptionsMonitor<ProcessUsageAnalysis> processUsageAnalysisMonitor,
            RocksDatabase rocksDatabase
            )
        {
            _logger = logger;
            _nodeSessionService = nodeSessionService;
            _dbContextFactory = dbContextFactory;
            _hearBeatMessageBatchQueue = heartBeatMessageBatchBlock;
            _memoryCache = memoryCache;
            _webServerOptions = optionsMonitor.CurrentValue;
            _rocksDatabase = rocksDatabase;
            _processUsageAnalysisMonitorToken = processUsageAnalysisMonitor.OnChange(OnProcessUsageAnalysisChanged);
            _processUsageAnalysis = processUsageAnalysisMonitor.CurrentValue;
        }

        private void OnProcessUsageAnalysisChanged(ProcessUsageAnalysis processUsageAnalysis, string? name)
        {
            _processUsageAnalysis = processUsageAnalysis;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {

            if (!_webServerOptions.DebugProductionMode)
            {
                await InvalidateAllNodeStatusAsync(stoppingToken);
            }

            await foreach (var arrayPoolCollection in _hearBeatMessageBatchQueue.ReceiveAllAsync(stoppingToken)
                                                                                     .ConfigureAwait(false))
            {
                Stopwatch stopwatch = new Stopwatch();
                int count = 0;
                try
                {
                    count = arrayPoolCollection.CountNotNull();
                    if (count == 0)
                    {
                        continue;
                    }

                    stopwatch.Start();
                    await ProcessHeartBeatMessagesAsync(arrayPoolCollection).ConfigureAwait(false);
                    _logger.LogInformation($"process {arrayPoolCollection.CountNotNull()} messages,spent: {stopwatch.Elapsed}, AvailableCount:{_hearBeatMessageBatchQueue.AvailableCount}");
                    stopwatch.Reset();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.ToString());
                }
                finally
                {
                    _logger.LogInformation($"process {count} messages, spent:{stopwatch.Elapsed}, available ount:{_hearBeatMessageBatchQueue.AvailableCount}");
                    stopwatch.Reset();
                    arrayPoolCollection.Dispose();
                }

            }
        }

        private async Task InvalidateAllNodeStatusAsync(CancellationToken stoppingToken)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            try
            {
                foreach (var item in dbContext.NodeInfoDbSet.AsQueryable().Where(x => x.Status == NodeStatus.Online))
                {
                    item.Status = NodeStatus.Offline;
                }
                await dbContext.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
            }
            finally
            {

            }
        }

        private async Task ProcessHeartBeatMessagesAsync(ArrayPoolCollection<NodeHeartBeatSessionMessage> arrayPoolCollection)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            Stopwatch stopwatch = new Stopwatch();
            try
            {

                foreach (var hearBeatSessionMessage in arrayPoolCollection)
                {
                    if (hearBeatSessionMessage == null)
                    {
                        continue;
                    }
                    stopwatch.Start();
                    await ProcessHeartBeatMessageAsync(dbContext, hearBeatSessionMessage).ConfigureAwait(false);
                    stopwatch.Stop();
                    this._logger.LogInformation($"process heartbeat {hearBeatSessionMessage.NodeSessionId} spent:{stopwatch.Elapsed}");
                    stopwatch.Reset();
                }

                stopwatch.Start();
                await dbContext.SaveChangesAsync().ConfigureAwait(false);
                stopwatch.Stop();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
            }
            finally
            {
                this._logger.LogInformation($"Process {arrayPoolCollection.CountNotNull()} messages, SaveElapsed:{stopwatch.Elapsed}");
                stopwatch.Reset();
            }
        }

        private async Task ProcessHeartBeatMessageAsync(
            ApplicationDbContext dbContext, 
            NodeHeartBeatSessionMessage hearBeatMessage
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
                nodeInfo = await dbContext.FindAsync<NodeInfoModel>(hearBeatMessage.NodeSessionId.NodeId.Value).ConfigureAwait(false);

                if (nodeInfo == null)
                {
                    return;
                }

                var nodeName = _nodeSessionService.GetNodeName(hearBeatMessage.NodeSessionId);
                var nodeStatus = _nodeSessionService.GetNodeStatus(hearBeatMessage.NodeSessionId);
                nodeInfo.Status = nodeStatus;

                if (hearBeatResponse != null)
                {
                    nodeInfo.Profile.UpdateTime =
              DateTime.ParseExact(hearBeatResponse.Properties[NodePropertyModel.LastUpdateDateTime_Key],
              NodePropertyModel.DateTimeFormatString, DateTimeFormatInfo.InvariantInfo);
                    nodeInfo.Profile.Name = nodeInfo.Name;
                    nodeInfo.Profile.NodeInfoId = nodeInfo.Id;
                    nodeInfo.Profile.ClientVersion = hearBeatResponse.Properties[NodePropertyModel.ClientVersion_Key];
                    nodeInfo.Profile.IpAddress = hearBeatResponse.Properties["RemoteIpAddress"];
                    nodeInfo.Profile.InstallStatus = true;
                    nodeInfo.Profile.LoginName = hearBeatResponse.Properties[NodePropertyModel.Environment_UserName_Key];
                    nodeInfo.Profile.FactoryName = hearBeatResponse.Properties[NodePropertyModel.FactoryName_key];

                    var propsDict = await _memoryCache.GetOrCreateAsync<ConcurrentDictionary<string, string>>("NodeProps:"+nodeInfo.Id, TimeSpan.FromHours(1)).ConfigureAwait(false);

                    foreach (var item in hearBeatResponse.Properties)
                    {
                        if (item.Key == null)
                        {
                            continue;
                        }
                        if (!propsDict.TryGetValue(item.Key, out var oldValue))
                        {
                            propsDict.TryAdd(item.Key, item.Value);
                        }
                        else
                        {
                            propsDict.TryUpdate(item.Key, item.Value, oldValue);
                        }
                    }

                    if (propsDict.Count > 0)
                    {
                        var nodePropertySnapshotModel =
                            new NodePropertySnapshotModel()
                            {
                                Id = Guid.NewGuid().ToString(),
                                Name = "Default",
                                CreationDateTime = nodeInfo.Profile.UpdateTime,
                                NodeProperties = [],
                                NodeInfoId = nodeInfo.Id
                            };
                        dbContext.Add(nodePropertySnapshotModel);
                        nodeInfo.LastNodePropertySnapshotId = nodePropertySnapshotModel.Id;
                        if (propsDict.TryGetValue(NodePropertyModel.Process_Processes_Key, out var processString))
                        {
                            if (!string.IsNullOrEmpty(processString) && processString.IndexOf('[') >= 0)
                            {
                                var processInfoList = JsonSerializer.Deserialize<ProcessInfo[]>(processString);
                                if (this._processUsageAnalysis != null && processInfoList != null)
                                {
                                    HashSet<string> usages = new HashSet<string>();
                                    foreach (var mapping in _processUsageAnalysis.Mappings)
                                    {
                                        if (processInfoList.Any(x => x.FileName.Contains(mapping.FileName)))
                                        {
                                            usages.Add(mapping.Name);
                                        }
                                    }
                                    nodeInfo.Profile.Usages = usages.Count == 0 ? null : string.Join(",", usages.OrderBy(x => x));
                                }

                            }
                        }
                    }
                }


            }
            catch (Exception ex)
            {
                _logger.LogError($"{ex}");
            }
            finally
            {

            }


        }

    }
}
