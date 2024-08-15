using Microsoft.AspNetCore.Http;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Services.Counters;
using System.Globalization;

namespace NodeService.WebServer.Services.NodeSessions.MessageHandlers;

public class HeartBeatResponseHandler : IMessageHandler
{
    readonly BatchQueue<NodeHeartBeatSessionMessage> _heartBeatBatchQueue;
    readonly BatchQueue<NodeStatusChangeRecordModel> _nodeStatusChangeRecordBatchQueue;
    readonly WebServerCounter _webServerCounter;
    readonly ILogger<HeartBeatResponseHandler> _logger;
    readonly INodeSessionService _nodeSessionService;
    string _remoteIpAddress;

    public HeartBeatResponseHandler(
        ILogger<HeartBeatResponseHandler> logger,
        INodeSessionService nodeSessionService,
        BatchQueue<NodeHeartBeatSessionMessage> heartBeatBatchQueue,
        BatchQueue<NodeStatusChangeRecordModel> nodeStatusChangeRecordBatchQueue,
        WebServerCounter webServerCounter)
    {
        _logger = logger;
        _nodeSessionService = nodeSessionService;
        _heartBeatBatchQueue = heartBeatBatchQueue;
        _nodeStatusChangeRecordBatchQueue = nodeStatusChangeRecordBatchQueue;
        _webServerCounter = webServerCounter;
        NodeSessionId = NodeSessionId.Empty;
    }

    public NodeSessionId NodeSessionId { get; set; }
    public HttpContext HttpContext { get; set; }

    public async ValueTask DisposeAsync()
    {
        EnsureRemoteIpAddress();
        if (NodeSessionId != NodeSessionId.Empty)
        {
            _nodeSessionService.UpdateNodeStatus(NodeSessionId, NodeStatus.Offline);
            await _heartBeatBatchQueue.SendAsync(new NodeHeartBeatSessionMessage
            {
                Message = null,
                NodeSessionId = NodeSessionId,
                IpAddress = _remoteIpAddress,
            });
        }

        _heartBeatBatchQueue.TriggerBatch();
    }

    public async ValueTask HandleAsync(IMessage message, CancellationToken cancellationToken = default)
    {
        EnsureRemoteIpAddress();
        _nodeSessionService.UpdateNodeStatus(NodeSessionId, NodeStatus.Online);
        _nodeSessionService.UpdateNodeIpAddress(NodeSessionId, _remoteIpAddress);
        var hostName = _nodeSessionService.GetNodeName(NodeSessionId);
        if (message is HeartBeatResponse heartBeatResponse)
        {
            heartBeatResponse.Properties.TryAdd("RemoteIpAddress", _remoteIpAddress);
            if (heartBeatResponse.Properties.TryGetValue(
                NodePropertyModel.LastUpdateDateTime_Key,
                out string dateTimeString)
                && DateTime.TryParseExact(
                dateTimeString,
                NodePropertyModel.DateTimeFormatString,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime dateTime)
                )
            {
                _nodeSessionService.UpdateClientDateTime(NodeSessionId, dateTime);
            }
            await _heartBeatBatchQueue.SendAsync(new NodeHeartBeatSessionMessage
            {
                Message = heartBeatResponse,
                NodeSessionId = NodeSessionId,
                HostName = hostName,
                IpAddress = _remoteIpAddress,
                UtcRecieveDateTime = DateTime.UtcNow,
            }, cancellationToken);
        }

    }

    private void EnsureRemoteIpAddress()
    {
        if (_remoteIpAddress == null)
        {
            _remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        }
    }

    public async ValueTask InitAsync(NodeSessionId nodeSessionId, CancellationToken cancellationToken = default)
    {
        EnsureRemoteIpAddress();
        NodeSessionId = nodeSessionId;
        await _nodeStatusChangeRecordBatchQueue.SendAsync(new NodeStatusChangeRecordModel()
        {
            Id = Guid.NewGuid().ToString(),
            CreationDateTime = DateTime.UtcNow,
            NodeId = NodeSessionId.NodeId.Value,
            Status = NodeStatus.Online,
            Message = $"Online",
            IpAddress = _remoteIpAddress
        }, cancellationToken);
    }
}