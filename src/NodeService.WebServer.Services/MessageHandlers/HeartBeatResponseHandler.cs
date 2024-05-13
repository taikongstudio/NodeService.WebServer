using Microsoft.AspNetCore.Http;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Services.NodeSessions;

namespace NodeService.WebServer.Services.MessageHandlers;

public class HeartBeatResponseHandler : IMessageHandler
{
    readonly BatchQueue<NodeHeartBeatSessionMessage> _heartBeatBatchQueue;
    readonly ILogger<HeartBeatResponseHandler> _logger;
    readonly INodeSessionService _nodeSessionService;

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

    public NodeSessionId NodeSessionId { get; set; }

    public async ValueTask DisposeAsync()
    {
        if (NodeSessionId != NodeSessionId.Empty)
        {
            _nodeSessionService.UpdateNodeStatus(NodeSessionId, NodeStatus.Offline);
            await _heartBeatBatchQueue.SendAsync(new NodeHeartBeatSessionMessage
            {
                Message = null,
                NodeSessionId = NodeSessionId
            });
            _heartBeatBatchQueue.TriggerBatch();
        }
    }

    public ValueTask HandleAsync(NodeSessionId nodeSessionId, HttpContext httpContext, IMessage message)
    {
        var heartBeapResponse = message as HeartBeatResponse;
        heartBeapResponse.Properties.TryAdd("RemoteIpAddress", httpContext.Connection.RemoteIpAddress.ToString());
        return HandleAsync(nodeSessionId, heartBeapResponse);
    }

    public async ValueTask HandleAsync(NodeSessionId nodeSessionId, HeartBeatResponse heartBeatResponse)
    {
        NodeSessionId = nodeSessionId;
        _nodeSessionService.UpdateNodeStatus(NodeSessionId, NodeStatus.Online);
        await _heartBeatBatchQueue.SendAsync(new NodeHeartBeatSessionMessage
        {
            Message = heartBeatResponse,
            NodeSessionId = nodeSessionId
        });
    }
}