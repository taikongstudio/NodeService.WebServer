using Microsoft.AspNetCore.Http;
using NodeService.Infrastructure.Models;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Services.NodeSessions;
using System.Net.Http;
using System.Net.NetworkInformation;

namespace NodeService.WebServer.Services.NodeSessions.MessageHandlers;

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
                CreationDateTime = DateTime.UtcNow,
                NodeId = NodeSessionId.NodeId.Value,
                Message = $"Offline"
            });
            await _heartBeatBatchQueue.SendAsync(new NodeHeartBeatSessionMessage
            {
                Message = null,
                NodeSessionId = NodeSessionId
            });
        }

        _heartBeatBatchQueue.TriggerBatch();
    }

    public async ValueTask HandleAsync(IMessage message, CancellationToken cancellationToken = default)
    {
        if (_remoteIpAddress == null)
        {
            _remoteIpAddress = HttpContext.Connection.RemoteIpAddress.ToString();
        }
        _nodeSessionService.UpdateNodeStatus(NodeSessionId, NodeStatus.Online);
        _nodeSessionService.UpdateNodeIpAddress(NodeSessionId, _remoteIpAddress);
        if (message is HeartBeatResponse heartBeatResponse)
        {
            heartBeatResponse.Properties.TryAdd("RemoteIpAddress", _remoteIpAddress);
            await _heartBeatBatchQueue.SendAsync(new NodeHeartBeatSessionMessage
            {
                Message = heartBeatResponse,
                NodeSessionId = NodeSessionId
            });
        }

    }

    public async ValueTask InitAsync(NodeSessionId nodeSessionId, CancellationToken cancellationToken = default)
    {
        NodeSessionId = nodeSessionId;
        await _nodeStatusChangeRecordBatchQueue.SendAsync(new NodeStatusChangeRecordModel()
        {
            Id = Guid.NewGuid().ToString(),
            CreationDateTime = DateTime.UtcNow,
            NodeId = NodeSessionId.NodeId.Value,
            Message = $"Online"
        }, cancellationToken);
    }
}