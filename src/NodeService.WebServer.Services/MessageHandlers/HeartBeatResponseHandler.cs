using Microsoft.AspNetCore.Http;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Services.NodeSessions;

namespace NodeService.WebServer.Services.MessageHandlers;

public class HeartBeatResponseHandler : IMessageHandler
{
    readonly BatchQueue<NodeHeartBeatSessionMessage> _heartBeatBatchQueue;
    readonly BatchQueue<NodeStatusChangeRecordModel> _nodeStatusChangeRecordBatchQueue;
    readonly ILogger<HeartBeatResponseHandler> _logger;
    readonly INodeSessionService _nodeSessionService;

    public HeartBeatResponseHandler(
        ILogger<HeartBeatResponseHandler> logger,
        INodeSessionService nodeSessionService,
        BatchQueue<NodeHeartBeatSessionMessage> heartBeatBatchQueue,
        BatchQueue<NodeStatusChangeRecordModel> nodeStatusChangeRecordBatchQueue)
    {
        _logger = logger;
        _nodeSessionService = nodeSessionService;
        _heartBeatBatchQueue = heartBeatBatchQueue;
        _nodeStatusChangeRecordBatchQueue = nodeStatusChangeRecordBatchQueue;
        NodeSessionId = NodeSessionId.Empty;
    }

    public NodeSessionId NodeSessionId { get; set; }

    public async ValueTask DisposeAsync()
    {
        if (NodeSessionId != NodeSessionId.Empty)
        {
            _nodeSessionService.UpdateNodeStatus(NodeSessionId, NodeStatus.Offline);
            await _nodeStatusChangeRecordBatchQueue.SendAsync(new NodeStatusChangeRecordModel()
            {
                Id = Guid.NewGuid().ToString(),
                DateTime = DateTime.UtcNow,
                NodeId = NodeSessionId.NodeId.Value,
                Message = $"Offline"
            });
            await _heartBeatBatchQueue.SendAsync(new NodeHeartBeatSessionMessage
            {
                Message = null,
                NodeSessionId = NodeSessionId
            });
            _heartBeatBatchQueue.TriggerBatch();
        }
    }

    public ValueTask HandleAsync(
        NodeSessionId nodeSessionId,
        HttpContext httpContext,
        IMessage message)
    {
        var heartBeapResponse = message as HeartBeatResponse;
        heartBeapResponse.Properties.TryAdd("RemoteIpAddress", httpContext.Connection.RemoteIpAddress.ToString());
        return HandleAsync(nodeSessionId, heartBeapResponse);
    }

    public async ValueTask HandleAsync(
        NodeSessionId nodeSessionId,
        HeartBeatResponse heartBeatResponse)
    {
        if (NodeSessionId.IsNullOrEmpty)
        {
            NodeSessionId = nodeSessionId;
            await _nodeStatusChangeRecordBatchQueue.SendAsync(new NodeStatusChangeRecordModel()
            {
                Id = Guid.NewGuid().ToString(),
                DateTime = DateTime.UtcNow,
                NodeId = NodeSessionId.NodeId.Value,
                Message = $"Online"
            });
        }

        _nodeSessionService.UpdateNodeStatus(NodeSessionId, NodeStatus.Online);
        await _heartBeatBatchQueue.SendAsync(new NodeHeartBeatSessionMessage
        {
            Message = heartBeatResponse,
            NodeSessionId = nodeSessionId
        });
    }
}