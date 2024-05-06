using Microsoft.AspNetCore.Http;
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

public record class JobExecutionReportMessage : NodeSessionMessage<JobExecutionReport>
{
}

public class NodeSessionService : INodeSessionService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly BatchQueue<NodeHeartBeatSessionMessage> _hearBeatBatchQueue;
    private readonly ILogger<NodeSessionService> _logger;
    private readonly NodeHealthyCounterDictionary _nodeHealthyCounterDictionary;
    private readonly ConcurrentDictionary<NodeSessionId, NodeSession> _nodeSessionDict;

    public NodeSessionService(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        ILogger<NodeSessionService> logger,
        BatchQueue<NodeHeartBeatSessionMessage> hearBeatBatchQueue,
        NodeHealthyCounterDictionary nodeHealthyCounterDictionary
    )
    {
        _dbContextFactory = dbContextFactory;
        _nodeSessionDict = new ConcurrentDictionary<NodeSessionId, NodeSession>();
        _logger = logger;
        _hearBeatBatchQueue = hearBeatBatchQueue;
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

    public async Task<IEnumerable<JobExecutionInstanceModel>> QueryTaskExecutionInstancesAsync(
        NodeId nodeId,
        QueryTaskExecutionInstancesParameters parameters,
        CancellationToken cancellationToken = default)
    {
        JobExecutionInstanceModel[] result = [];
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var queryable =
            dbContext
                .JobExecutionInstancesDbSet
                .AsQueryable();
        var id = nodeId.Value;
        if (nodeId != NodeId.Any) queryable = queryable.Where(x => x.NodeInfoId == id);
        if (!string.IsNullOrEmpty(parameters.Id))
        {
            var jobScheduleConfigId = parameters.Id;
            queryable = queryable.Where(x => x.JobScheduleConfigId == jobScheduleConfigId);
        }

        if (!string.IsNullOrEmpty(parameters.FireInstanceId))
            queryable.Where(x => x.FireInstanceId == parameters.FireInstanceId);
        if (parameters.Status != null)
        {
            var status = parameters.Status.Value;
            queryable = queryable.Where(x => x.Status == status);
        }

        if (parameters.BeginTime != null && parameters.EndTime == null)
        {
            var beginTime = parameters.BeginTime.Value;
            queryable = queryable.Where(x => x.FireTimeUtc >= beginTime);
        }

        if (parameters.BeginTime == null && parameters.EndTime != null)
        {
            var endTime = parameters.EndTime.Value;
            queryable = queryable.Where(x => x.FireTimeUtc <= endTime);
        }
        else if (parameters.BeginTime != null && parameters.EndTime != null)
        {
            var beginTime = parameters.BeginTime.Value;
            var endTime = parameters.EndTime.Value;
            queryable = queryable.Where(x => x.FireTimeUtc >= beginTime && x.FireTimeUtc <= endTime);
        }

        result = await queryable.ToArrayAsync(cancellationToken);
        return result;
    }

    public void UpdateNodeStatus(NodeSessionId nodeSessionId, NodeStatus nodeStatus)
    {
        if (nodeStatus == NodeStatus.Offline) _nodeHealthyCounterDictionary.Ensure(nodeSessionId).OfflineCount++;
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

    public async Task<JobExecutionEventResponse?> SendJobExecutionEventAsync(
        NodeSessionId nodeSessionId,
        JobExecutionEventRequest taskExecutionEventRequest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodeSessionId);
        ArgumentNullException.ThrowIfNull(taskExecutionEventRequest);
        var subscribeEvent = new SubscribeEvent
        {
            RequestId = taskExecutionEventRequest.RequestId,
            Timeout = TimeSpan.FromSeconds(30),
            Topic = "job",
            JobExecutionEventRequest = taskExecutionEventRequest
        };
        var rsp = await this.SendMessageAsync<SubscribeEvent, JobExecutionEventResponse>(
            nodeSessionId,
            subscribeEvent,
            cancellationToken);
        return rsp;
    }

    public async Task<NodeId> EnsureNodeInfoAsync(
        NodeSessionId nodeSessionId,
        string nodeName,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var nodeId = nodeSessionId.NodeId.Value;
        var nodeInfo = await dbContext.NodeInfoDbSet.AsQueryable()
            .FirstOrDefaultAsync(x => x.Id == nodeId);

        if (nodeInfo == null)
        {
            nodeInfo = NodeInfoModel.Create(nodeId, nodeName);
            var nodeProfile = await dbContext.NodeProfilesDbSet.OrderByDescending(x => x.ServerUpdateTimeUtc)
                .FirstOrDefaultAsync(
                    x => x.Name == nodeName,
                    cancellationToken);
            if (nodeProfile != null)
            {
                var oldNodeInfo = await dbContext.NodeInfoDbSet.FirstOrDefaultAsync(
                    x => x.Id == nodeProfile.NodeInfoId,
                    cancellationToken);
                if (oldNodeInfo != null) dbContext.NodeInfoDbSet.Remove(oldNodeInfo);
                nodeProfile.NodeInfoId = nodeId;
                nodeInfo.Profile = nodeProfile;
                nodeInfo.ProfileId = nodeInfo.Profile.Id;
            }

            await dbContext.NodeInfoDbSet.AddAsync(nodeInfo, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new NodeId(nodeInfo.Id);
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

    public async Task<JobExecutionInstanceModel> AddJobExecutionInstanceAsync(
        NodeSessionId nodeSessionId,
        FireTaskParameters parameters,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var nodeName = GetNodeName(nodeSessionId);
        var taskExecutionInstance = new JobExecutionInstanceModel
        {
            Id = Guid.NewGuid().ToString(),
            Name = $"{nodeName} {parameters.TaskScheduleConfig.Name} {parameters.FireInstanceId}",
            NodeInfoId = nodeSessionId.NodeId.Value,
            Status = JobExecutionStatus.Triggered,
            FireTimeUtc = parameters.FireTimeUtc.DateTime,
            Message = string.Empty,
            FireType = "Server",
            TriggerSource = parameters.TriggerSource,
            JobScheduleConfigId = parameters.TaskScheduleConfig.Id,
            ParentId = parameters.ParentTaskId,
            FireInstanceId = parameters.FireInstanceId
        };


        var isOnline = GetNodeStatus(nodeSessionId) == NodeStatus.Online;
        if (!isOnline)
        {
            taskExecutionInstance.Message = $"{nodeName} offline";
            taskExecutionInstance.Status = JobExecutionStatus.Failed;
        }
        else
        {
            switch (parameters.TaskScheduleConfig.ExecutionStrategy)
            {
                case JobExecutionStrategy.Concurrent:
                    taskExecutionInstance.Message = $"{nodeName}:triggered";
                    break;
                case JobExecutionStrategy.Queue:
                    taskExecutionInstance.Status = JobExecutionStatus.Pendding;
                    taskExecutionInstance.Message = $"{nodeName}: waiting for any job";
                    break;
                case JobExecutionStrategy.Skip:
                    taskExecutionInstance.Status = JobExecutionStatus.Pendding;
                    taskExecutionInstance.Message = $"{nodeName}: start job";
                    break;
                case JobExecutionStrategy.Stop:
                    taskExecutionInstance.Status = JobExecutionStatus.Pendding;
                    taskExecutionInstance.Message = $"{nodeName}: waiting for kill all job";
                    break;
            }
        }


        var taskScheduleConfigJsonString = parameters.TaskScheduleConfig.ToJsonString<JobScheduleConfigModel>();

        if (parameters.NextFireTimeUtc != null)
            taskExecutionInstance.NextFireTimeUtc = parameters.NextFireTimeUtc.Value.UtcDateTime;
        if (parameters.PreviousFireTimeUtc != null)
            taskExecutionInstance.NextFireTimeUtc = parameters.PreviousFireTimeUtc.Value.UtcDateTime;
        if (parameters.ScheduledFireTimeUtc != null)
            taskExecutionInstance.ScheduledFireTimeUtc = parameters.ScheduledFireTimeUtc.Value.UtcDateTime;


        await dbContext.JobExecutionInstancesDbSet.AddAsync(taskExecutionInstance, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return taskExecutionInstance;
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
            OutputQueue = new AsyncQueue<IMessage>();
        }

        public IAsyncQueue<IMessage> InputQueue { get; }

        public IAsyncQueue<IMessage> OutputQueue { get; }

        public NodeStatus Status { get; set; }

        public string Name { get; set; }

        public NodeSessionId Id { get; private set; }

        public DateTime LastHeartBeatOutputDateTime { get; set; }

        public DateTime LastHeartBeatInputDateTime { get; set; }

        public HttpContext? HttpContext { get; internal set; }

        public void Reset()
        {
        }
    }
}