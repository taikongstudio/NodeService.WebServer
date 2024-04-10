using Microsoft.AspNetCore.Http;
using NodeService.WebServer.Services.NodeSessions;

namespace NodeService.WebServer.Services.MessageHandlers
{

    public class HeartBeatResponseHandler : IMessageHandler
    {
        private readonly INodeSessionService _nodeSessionService;
        private readonly ILogger<HeartBeatResponseHandler> _logger;
        private readonly BatchQueue<NodeHeartBeatSessionMessage> _heartBeatBatchQueue;

        public HeartBeatResponseHandler(
            INodeSessionService nodeSessionService,
            IMemoryCache memoryCache,
            ILogger<HeartBeatResponseHandler> logger,
            BatchQueue<NodeHeartBeatSessionMessage> heartBeatBatchBlock)
        {
            _nodeSessionService = nodeSessionService;
            _logger = logger;
            _heartBeatBatchQueue = heartBeatBatchBlock;
            NodeSessionId = NodeSessionId.Empty;
        }

        public NodeSessionId NodeSessionId { get; private set; }

        public async ValueTask DisposeAsync()
        {
            if (NodeSessionId != NodeSessionId.Empty)
            {
                _nodeSessionService.UpdateNodeStatus(NodeSessionId, NodeStatus.Offline);
                await _heartBeatBatchQueue.SendAsync(new NodeHeartBeatSessionMessage()
                {
                    Message = null,
                    NodeSessionId = NodeSessionId
                });
                _heartBeatBatchQueue.TriggerBatch();
            }
        }

        public async ValueTask HandleAsync(NodeSessionId nodeSessionId, HeartBeatResponse heartBeatResponse)
        {
            NodeSessionId = nodeSessionId;
            _nodeSessionService.UpdateNodeStatus(NodeSessionId, NodeStatus.Online);
            await _heartBeatBatchQueue.SendAsync(new NodeHeartBeatSessionMessage()
            {
                Message = heartBeatResponse,
                NodeSessionId = nodeSessionId
            });
        }

        public ValueTask HandleAsync(NodeSessionId nodeSessionId, HttpContext httpContext, IMessage message)
        {
            var heartBeapResponse = message as HeartBeatResponse;
            heartBeapResponse.Properties.TryAdd("RemoteIpAddress", httpContext.Connection.RemoteIpAddress.ToString());
            return HandleAsync(nodeSessionId, heartBeapResponse);
        }
    }

}
