using System.Buffers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NodeService.WebServer.Models;

namespace NodeService.WebServer.Services.NodeSessions;

public class HeartBeatRequestProducerService : BackgroundService
{
    readonly ILogger<HeartBeatRequestProducerService> _logger;

    readonly INodeSessionService _nodeSessionService;
    readonly IAsyncQueue<NotificationMessage> _notificationMessageQueue;
    readonly IOptionsMonitor<WebServerOptions> _optionsMonitor;
    readonly IDisposable? _token;
    WebServerOptions _options;
    readonly ExceptionCounter _exceptionCounter;

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
    }

    void OnOptionsChange(WebServerOptions options, string value)
    {
        _options = options;
    }

    public override void Dispose()
    {
        _token.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
            try
            {
                await ExecuteCoreAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
    }

    async Task ExecuteCoreAsync(CancellationToken stoppingToken = default)
    {
        var nodeSessions = _nodeSessionService.EnumNodeSessions(NodeId.Any);
        if (!nodeSessions.Any())
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(5));
        var nodeSessionsCount = _nodeSessionService.GetNodeSessionsCount();
        var nodeSessionArray = ArrayPool<NodeSessionId>.Shared.Rent(nodeSessionsCount);
        var nodeCount = 0;
        foreach (var nodeSessionId in nodeSessions)
        {
            nodeSessionArray[nodeCount] = nodeSessionId;
            nodeCount++;
        }

        Random.Shared.Shuffle(nodeSessionArray);
        _logger.LogInformation($"Start Count:{nodeSessionsCount}");
        foreach (var nodeSessionId in nodeSessionArray)
        {
            if (nodeSessionId.NodeId.IsNullOrEmpty) continue;
            if (_nodeSessionService.GetNodeStatus(nodeSessionId) != NodeStatus.Online)
            {
                TryAbortConnection(nodeSessionId);
                continue;
            }

            var nodeName = _nodeSessionService.GetNodeName(nodeSessionId);
            _logger.LogInformation($"Send heart beat to {nodeSessionId}:{nodeName}");
            await _nodeSessionService.PostHeartBeatRequestAsync(nodeSessionId);
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(50, (double)_options.HeartBeatPeriod / nodeCount)),
                stoppingToken);
        }

        _logger.LogInformation("End");
        ArrayPool<NodeSessionId>.Shared.Return(nodeSessionArray, true);
        await _nodeSessionService.InvalidateAllNodeStatusAsync();
    }

    void TryAbortConnection(NodeSessionId nodeSessionId)
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

    class HeartBeatCounter
    {
        public long Count { get; set; }
        public NodeSessionId SessionId { get; init; }
    }
}