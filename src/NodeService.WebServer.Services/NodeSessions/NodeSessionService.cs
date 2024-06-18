using Microsoft.AspNetCore.Http;
using NodeService.Infrastructure.Messages;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data;

namespace NodeService.WebServer.Services.NodeSessions;

public abstract record class NodeSessionMessage
{
    public NodeSessionId NodeSessionId { get; init; }

    public INodeMessage Message { get; init; }
}

public abstract record class NodeSessionMessage<T> : NodeSessionMessage where T : INodeMessage
{
    public T GetMessage()
    {
        return (T)Message;
    }
}

public record class NodeHeartBeatSessionMessage : NodeSessionMessage<HeartBeatResponse>
{
}

public record class TaskExecutionReportMessage : NodeSessionMessage<TaskExecutionReport>
{
}

public record class FileSystemWatchEventReportMessage : NodeSessionMessage<FileSystemWatchEventReport>
{
}

public class NodeSessionService : INodeSessionService
{
    private readonly ILogger<NodeSessionService> _logger;
    private readonly NodeHealthyCounterDictionary _nodeHealthyCounterDictionary;
    private readonly ConcurrentDictionary<NodeSessionId, NodeSession> _nodeSessionDict;

    public NodeSessionService(
        ILogger<NodeSessionService> logger,
        BatchQueue<NodeHeartBeatSessionMessage> hearBeatBatchQueue,
        NodeHealthyCounterDictionary nodeHealthyCounterDictionary
    )
    {
        _nodeSessionDict = new ConcurrentDictionary<NodeSessionId, NodeSession>();
        _logger = logger;
        _nodeHealthyCounterDictionary = nodeHealthyCounterDictionary;
    }

    public IAsyncQueue<IMessage> GetInputQueue(NodeSessionId nodeSessionId)
    {
        return EnsureNodeSession(nodeSessionId).InputQueue;
    }

    public NodeStatus GetNodeStatus(NodeSessionId nodeSessionId)
    {
        return EnsureNodeSession(nodeSessionId).Status;
    }

    public DateTime GetLastHeartBeatOutputDateTime(NodeSessionId nodeSessionId)
    {
        return EnsureNodeSession(nodeSessionId).LastHeartBeatOutputDateTime;
    }

    public DateTime GetLastHeartBeatInputDateTime(NodeSessionId nodeSessionId)
    {
        return EnsureNodeSession(nodeSessionId).LastHeartBeatInputDateTime;
    }

    public IAsyncQueue<IMessage> GetOutputQueue(NodeSessionId nodeSessionId)
    {
        return EnsureNodeSession(nodeSessionId).OutputQueue;
    }


    public void UpdateNodeStatus(NodeSessionId nodeSessionId, NodeStatus nodeStatus)
    {
        if (nodeStatus == NodeStatus.Offline) _nodeHealthyCounterDictionary.Ensure(nodeSessionId.NodeId).OfflineCount++;
        EnsureNodeSession(nodeSessionId).Status = nodeStatus;
    }

    public async Task PostHeartBeatRequestAsync(
        NodeSessionId nodeSessionId,
        CancellationToken cancellationToken = default)
    {
        EnsureNodeSession(nodeSessionId).LastHeartBeatOutputDateTime = DateTime.UtcNow;
        await this.PostMessageAsync(nodeSessionId, new SubscribeEvent
        {
            RequestId = Guid.NewGuid().ToString(),
            Timeout = TimeSpan.FromMinutes(10),
            HeartBeatRequest = new HeartBeatRequest
            {
                RequestId = Guid.NewGuid().ToString()
            }
        }, cancellationToken);
    }

    public async Task WriteHeartBeatResponseAsync(
        NodeSessionId nodeSessionId,
        HeartBeatResponse heartBeatResponse,
        CancellationToken cancellationToken = default)
    {
        EnsureNodeSession(nodeSessionId).LastHeartBeatInputDateTime = DateTime.UtcNow;
        await GetInputQueue(nodeSessionId).EnqueueAsync(
            heartBeatResponse,
            cancellationToken);
    }

    public async Task<TaskExecutionEventResponse?> SendTaskExecutionEventAsync(
        NodeSessionId nodeSessionId,
        TaskExecutionEventRequest taskExecutionEventRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(taskExecutionEventRequest);
        var subscribeEvent = new SubscribeEvent
        {
            RequestId = taskExecutionEventRequest.RequestId,
            Timeout = TimeSpan.FromHours(6),
            Topic = "task",
            TaskExecutionEventRequest = taskExecutionEventRequest
        };
        var rsp = await this.SendMessageAsync<SubscribeEvent, TaskExecutionEventResponse>(
            nodeSessionId,
            subscribeEvent,
            cancellationToken);
        return rsp;
    }

    public async ValueTask PostTaskExecutionEventAsync(
        NodeSessionId nodeSessionId,
        TaskExecutionEventRequest taskExecutionEventRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(taskExecutionEventRequest);
        var subscribeEvent = new SubscribeEvent
        {
            RequestId = taskExecutionEventRequest.RequestId,
            Timeout = TimeSpan.FromSeconds(30),
            Topic = "task",
            TaskExecutionEventRequest = taskExecutionEventRequest
        };

        await this.PostMessageAsync<SubscribeEvent>(
            nodeSessionId,
            subscribeEvent,
            cancellationToken);
    }

    public IEnumerable<NodeSessionId> EnumNodeSessions(NodeId nodeId)
    {
        foreach (var nodeSessionId in _nodeSessionDict.Keys)
        {
            if (nodeId == NodeId.Any) yield return nodeSessionId;
            if (nodeSessionId.NodeId == nodeId) yield return nodeSessionId;
        }
    }

    public string GetNodeName(NodeSessionId nodeSessionId)
    {
        return EnsureNodeSession(nodeSessionId).Name;
    }

    public void UpdateNodeName(NodeSessionId nodeSessionId, string nodeName)
    {
        EnsureNodeSession(nodeSessionId).Name = nodeName;
    }


    public int GetNodeSessionsCount()
    {
        return _nodeSessionDict.Count;
    }

    public ValueTask InvalidateAllNodeStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var nodeSessionId in EnumNodeSessions(NodeId.Any))
                if (GetNodeStatus(nodeSessionId) == NodeStatus.Offline)
                    ResetSession(nodeSessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
        }

        return ValueTask.CompletedTask;
    }

    public void SetHttpContext(NodeSessionId nodeSessionId, HttpContext? httpContext)
    {
        EnsureNodeSession(nodeSessionId).HttpContext = httpContext;
    }

    public HttpContext? GetHttpContext(NodeSessionId nodeSessionId)
    {
        return EnsureNodeSession(nodeSessionId).HttpContext;
    }

    public void ResetSession(NodeSessionId nodeSessionId)
    {
        if (_nodeSessionDict.TryGetValue(nodeSessionId, out var nodeSession)) nodeSession.Reset();
    }

    private NodeSession EnsureNodeSession(NodeSessionId nodeSessionId)
    {
        return _nodeSessionDict.GetOrAdd(nodeSessionId, CreateNodeSession);
    }

    private NodeSession CreateNodeSession(NodeSessionId nodeSessionId)
    {
        NodeSession nodeSession = new(nodeSessionId);
        return nodeSession;
    }

    private class NodeSession
    {
        public NodeSession(NodeSessionId id)
        {
            Id = id;
            Status = NodeStatus.NotConfigured;
            InputQueue = new AsyncQueue<IMessage>();
            OutputQueue = new AsyncQueue<IMessage>(1024);
        }

        public IAsyncQueue<IMessage> InputQueue { get; }

        public IAsyncQueue<IMessage> OutputQueue { get; }

        public NodeStatus Status { get; set; }

        public string Name { get; set; }

        public NodeSessionId Id { get; set; }

        public DateTime LastHeartBeatOutputDateTime { get; set; }

        public DateTime LastHeartBeatInputDateTime { get; set; }

        public HttpContext? HttpContext { get; internal set; }

        public void Reset()
        {
        }
    }
}