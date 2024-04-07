using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodeService.WebServer.Models;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services
{
    public class HeartBeatRequestProducerService : BackgroundService
    {
        private class HeartBeatCounter
        {
            public long Count { get; set; }
            public NodeSessionId SessionId { get; init; }
        }

        private readonly INodeSessionService _nodeSessionService;
        private readonly ILogger<HeartBeatRequestProducerService> _logger;
        private readonly IOptionsMonitor<WebServerOptions> _optionsMonitor;
        private readonly IDisposable? _token;
        private WebServerOptions _options;

        public HeartBeatRequestProducerService(
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
        }

        private void OnOptionsChange(WebServerOptions options, string value)
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
            {
                try
                {
                    await ExecuteCoreAsync(stoppingToken);

                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.ToString());
                }

            }
        }

        private async Task ExecuteCoreAsync(CancellationToken stoppingToken = default)
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
            int nodeCount = 0;
            foreach (var nodeSessionId in nodeSessions)
            {
                nodeSessionArray[nodeCount] = nodeSessionId;
                nodeCount++;
            }
            Random.Shared.Shuffle(nodeSessionArray);
            _logger.LogInformation($"Start Count:{nodeSessionsCount}");
            foreach (var nodeSessionId in nodeSessionArray)
            {
                if (nodeSessionId.NodeId.IsNullOrEmpty)
                {
                    continue;
                }
                if (_nodeSessionService.GetNodeStatus(nodeSessionId) != NodeStatus.Online)
                {
                    TryAbortConnection(nodeSessionId);
                    continue;
                }
                var nodeName = _nodeSessionService.GetNodeName(nodeSessionId);
                _logger.LogInformation($"Send heart beat to {nodeSessionId}:{nodeName}");
                await _nodeSessionService.PostHeartBeatRequestAsync(nodeSessionId);
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(50, (double)_options.HeartBeatPeriod / nodeCount)), stoppingToken);
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
}
