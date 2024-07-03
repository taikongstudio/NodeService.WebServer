using System.Buffers;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Services.NodeSessions;

public class HeartBeatRequestProducerService : BackgroundService
{
    private readonly ExceptionCounter _exceptionCounter;
    private readonly ILogger<HeartBeatRequestProducerService> _logger;

    private readonly INodeSessionService _nodeSessionService;
    private readonly IAsyncQueue<NotificationMessage> _notificationMessageQueue;
    private readonly IOptionsMonitor<WebServerOptions> _optionsMonitor;
    private readonly IDisposable? _token;
    private WebServerOptions _options;
    private ActionBlock<NodeSessionId> _pingQueue;

    public HeartBeatRequestProducerService(
        ExceptionCounter exceptionCounter,
        INodeSessionService nodeSessionService,
        ILogger<HeartBeatRequestProducerService> logger,
        IOptionsMonitor<WebServerOptions> optionsMonitor
    )
    {
        _nodeSessionService = nodeSessionService;
        _logger = logger;
        _optionsMonitor = optionsMonitor;
        _token = _optionsMonitor.OnChange(OnOptionsChange);
        _options = _optionsMonitor.CurrentValue;
        _exceptionCounter = exceptionCounter;
        _pingQueue = new ActionBlock<NodeSessionId>(PingOfflineNodeAsync, new ExecutionDataflowBlockOptions()
        {
            MaxDegreeOfParallelism = 4
        });
    }

    private async Task PingOfflineNodeAsync(NodeSessionId nodeSessionId)
    {
        try
        {
            using Ping ping = new Ping();
            var ipAddressOrHostName = _nodeSessionService.GetNodeIpAddress(nodeSessionId);
            if (ipAddressOrHostName == null)
            {
                return;
            }
            var reply = await ping.SendPingAsync(ipAddressOrHostName, TimeSpan.FromSeconds(5));
            var lastPingReplyInfo = new PingReplyInfo()
            {
                Status = reply.Status,
                RoundtripTime = reply.RoundtripTime,
                DateTime = DateTime.UtcNow
            };
            _nodeSessionService.UpdateNodePingReply(nodeSessionId, lastPingReplyInfo);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogInformation(ex.ToString());
        }

    }

    private void OnOptionsChange(WebServerOptions options, string? value)
    {
        _options = options;
    }

    public override void Dispose()
    {
        _token?.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
            try
            {
                await ExecuteCoreAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
    }

    private async Task ExecuteCoreAsync(CancellationToken cancellationToken = default)
    {
        var nodeSessions = _nodeSessionService.EnumNodeSessions(NodeId.Any);
        if (!nodeSessions.Any())
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        var nodeSessionsCount = _nodeSessionService.GetNodeSessionsCount();
        var nodeSessionArray = ArrayPool<NodeSessionId>.Shared.Rent(nodeSessionsCount);
        var nodeCount = 0;
        foreach (var nodeSessionId in nodeSessions)
        {
            nodeSessionArray[nodeCount] = nodeSessionId;
            nodeCount++;
        }

        Random.Shared.Shuffle(nodeSessionArray);
        Random.Shared.Shuffle(nodeSessionArray);
        Random.Shared.Shuffle(nodeSessionArray);
        _logger.LogInformation($"Start Count:{nodeSessionsCount}");

        foreach (var nodeSessionId in nodeSessionArray)
        {
            if (nodeSessionId.NodeId.IsNullOrEmpty) continue;

            bool pingOffNode = false;
            if (_nodeSessionService.GetNodeStatus(nodeSessionId) != NodeStatus.Online)
            {
                pingOffNode = true;
            }
            var pingReplyInfo = _nodeSessionService.GetNodeLastPingReplyInfo(nodeSessionId);
            if ((pingOffNode || pingReplyInfo == null) && DateTime.UtcNow - pingReplyInfo?.DateTime > TimeSpan.FromMinutes(2))
            {
                _nodeSessionService.UpdateNodePingReply(nodeSessionId, new PingReplyInfo()
                {
                    Status = IPStatus.Unknown,
                    RoundtripTime = 0,
                    DateTime = DateTime.UtcNow
                });
                _pingQueue.Post(nodeSessionId);
            }

            var nodeName = _nodeSessionService.GetNodeName(nodeSessionId);
            _logger.LogInformation($"Send heart beat to {nodeSessionId}:{nodeName}");
            await _nodeSessionService.PostHeartBeatRequestAsync(nodeSessionId, cancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(50, (double)_options.HeartBeatPeriod / nodeCount)),
                cancellationToken);
        }

        _logger.LogInformation("End");
        ArrayPool<NodeSessionId>.Shared.Return(nodeSessionArray, true);
        await _nodeSessionService.InvalidateAllNodeStatusAsync();
    }

    private void TryAbortConnection(NodeSessionId nodeSessionId)
    {
        try
        {
            _nodeSessionService.GetHttpContext(nodeSessionId)?.Abort();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
        }
        finally
        {
            _nodeSessionService.SetHttpContext(nodeSessionId, null);
        }
    }
}