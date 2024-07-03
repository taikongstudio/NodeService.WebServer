using Microsoft.AspNetCore.Http;
using NodeService.Infrastructure.Models;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Services.NodeSessions;
using System.Net.Http;
using System.Net.NetworkInformation;

namespace NodeService.WebServer.Services.MessageHandlers;

public class HeartBeatResponseHandler : IMessageHandler
{
    readonly BatchQueue<NodeHeartBeatSessionMessage> _heartBeatBatchQueue;
    readonly BatchQueue<NodeStatusChangeRecordModel> _nodeStatusChangeRecordBatchQueue;
    readonly ILogger<HeartBeatResponseHandler> _logger;
    readonly INodeSessionService _nodeSessionService;
    string _remoteIpAddress;

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
    public HttpContext HttpContext { get; set; }

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

    public async ValueTask HandleAsync(NodeSessionId nodeSessionId, IMessage message, CancellationToken cancellationToken)
    {
        if (_remoteIpAddress == null)
        {
            _remoteIpAddress = HttpContext.Connection.RemoteIpAddress.ToString();
        }
        var heartBeatResponse = message as HeartBeatResponse;
        heartBeatResponse.Properties.TryAdd("RemoteIpAddress", _remoteIpAddress);
        if (NodeSessionId.IsNullOrEmpty)
        {
            NodeSessionId = nodeSessionId;
            await _nodeStatusChangeRecordBatchQueue.SendAsync(new NodeStatusChangeRecordModel()
            {
                Id = Guid.NewGuid().ToString(),
                DateTime = DateTime.UtcNow,
                NodeId = NodeSessionId.NodeId.Value,
                Message = $"Online"
            }, cancellationToken);
        }

        _nodeSessionService.UpdateNodeStatus(NodeSessionId, NodeStatus.Online);
        _nodeSessionService.UpdateNodeIpAddress(NodeSessionId, _remoteIpAddress);
        await _heartBeatBatchQueue.SendAsync(new NodeHeartBeatSessionMessage
        {
            Message = heartBeatResponse,
            NodeSessionId = nodeSessionId
        });
    }

}