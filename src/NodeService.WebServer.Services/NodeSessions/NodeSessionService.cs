using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NodeService.Infrastructure.Messages;
using NodeService.Infrastructure.Models;
using NodeService.WebServer.Data;
using NodeService.WebServer.Services.MessageHandlers;
using System.Collections.Immutable;
using System.Net;
using System.Security.Cryptography.Xml;
using static NodeService.Infrastructure.Models.JobExecutionReport.Types;

namespace NodeService.WebServer.Services.NodeSessions
{
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
        private class NodeSession
        {
            public IAsyncQueue<IMessage> InputQueue { get; private set; }

            public IAsyncQueue<IMessage> OutputQueue { get; private set; }

            public NodeStatus Status { get; set; }

            public string Name { get; set; }

            public NodeSessionId Id { get; private set; }

            public DateTime LastHeartBeatOutputDateTime { get; set; }

            public DateTime LastHeartBeatInputDateTime { get; set; }

            public HttpContext? HttpContext { get; internal set; }

            public NodeSession(NodeSessionId id)
            {
                Id = id;
                Status = NodeStatus.NotConfigured;
                InputQueue = new AsyncQueue<IMessage>();
                OutputQueue = new AsyncQueue<IMessage>();

            }

            public void Reset()
            {
                Status = NodeStatus.Offline;
            }
        }


        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ConcurrentDictionary<NodeSessionId, NodeSession> _nodeSessionDict;
        private readonly ILogger<NodeSessionService> _logger;
        private readonly BatchQueue<NodeHeartBeatSessionMessage> _hearBeatBatchQueue;
        private readonly NodeHealthyCounterDictionary _nodeHealthyCounterDictionary;

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

        public void ResetSession(NodeSessionId nodeSessionId)
        {
            if (_nodeSessionDict.TryGetValue(nodeSessionId, out var nodeSession))
            {
                nodeSession.Reset();
            }
        }

        public async Task<IEnumerable<JobExecutionInstanceModel>> QueryJobExecutionInstances(
            NodeId nodeSessionId,
            QueryJobExecutionInstancesParameters parameters,
            CancellationToken cancellationToken = default)
        {
            JobExecutionInstanceModel[] result = [];
            using var dbContext = _dbContextFactory.CreateDbContext();
            var queryable =
                 dbContext
                .JobExecutionInstancesDbSet
                .AsQueryable();
            string id = nodeSessionId.Value;
            if (nodeSessionId != NodeId.Any)
            {
                queryable = queryable.Where(x => x.NodeInfoId == id);
            }
            if (!string.IsNullOrEmpty(parameters.Id))
            {
                string jobScheduleConfigId = parameters.Id;
                queryable = queryable.Where(x => x.JobScheduleConfigId == jobScheduleConfigId);
            }
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
            result = await queryable.ToArrayAsync();
            return result;
        }

        public void UpdateNodeStatus(NodeSessionId nodeSessionId, NodeStatus nodeStatus)
        {
            if (nodeStatus == NodeStatus.Offline)
            {
                _nodeHealthyCounterDictionary.Ensure(nodeSessionId).OfflineCount++;
            }
            EnsureNodeSession(nodeSessionId).Status = nodeStatus;
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

        public async Task PostHeartBeatRequestAsync(NodeSessionId nodeSessionId)
        {
            EnsureNodeSession(nodeSessionId).LastHeartBeatOutputDateTime = DateTime.UtcNow;
            await this.PostMessageAsync(nodeSessionId, new SubscribeEvent()
            {
                RequestId = Guid.NewGuid().ToString(),
                HeartBeatRequest = new HeartBeatRequest()
                {
                    RequestId = Guid.NewGuid().ToString(),
                }
            });
        }

        public async Task WriteHeartBeatResponseAsync(NodeSessionId nodeSessionId, HeartBeatResponse heartBeatResponse)
        {
            EnsureNodeSession(nodeSessionId).LastHeartBeatInputDateTime = DateTime.UtcNow;
            await GetInputQueue(nodeSessionId).EnqueueAsync(heartBeatResponse);
        }

        public async Task<JobExecutionEventResponse?> SendJobExecutionEventAsync(
            NodeSessionId nodeSessionId,
            JobExecutionEventRequest jobExecutionEventRequest,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(nodeSessionId);
            ArgumentNullException.ThrowIfNull(jobExecutionEventRequest);
            var subscribeEvent = new SubscribeEvent()
            {
                RequestId = jobExecutionEventRequest.RequestId,
                Timeout = TimeSpan.FromSeconds(30),
                Topic = "job",
                JobExecutionEventRequest = jobExecutionEventRequest,
            };
            var rsp = await this.SendMessageAsync<SubscribeEvent, JobExecutionEventResponse>(
                nodeSessionId,
                subscribeEvent,
                cancellationToken);
            return rsp;
        }

        public async Task<NodeId> EnsureNodeInfoAsync(NodeSessionId nodeSessionId, string nodeName)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var nodeId = nodeSessionId.NodeId.Value;
            var nodeInfo = await dbContext.NodeInfoDbSet.AsQueryable()
                .FirstOrDefaultAsync(x => x.Id == nodeId);

            if (nodeInfo == null)
            {
                nodeInfo = NodeInfoModel.Create(nodeId, nodeName);
                await dbContext.NodeInfoDbSet.AddAsync(nodeInfo);
                await dbContext.SaveChangesAsync();
            }
            return new NodeId(nodeInfo.Id);
        }

        public IEnumerable<NodeSessionId> EnumNodeSessions(NodeId nodeId)
        {
            foreach (var nodeSessionId in _nodeSessionDict.Keys)
            {
                if (nodeId == NodeId.Any)
                {
                    yield return nodeSessionId;
                }
                if (nodeSessionId.NodeId == nodeId)
                {
                    yield return nodeSessionId;
                }
            }
            yield break;
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
            string? parentId,
            JobFireParameters parameters)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var nodeName = GetNodeName(nodeSessionId);
            var jobExecutionInstance = new JobExecutionInstanceModel
            {
                Id = Guid.NewGuid().ToString(),
                Name = $"{nodeName} {parameters.JobScheduleConfig.Name} {parameters.FireInstanceId}",
                NodeInfoId = nodeSessionId.NodeId.Value,
                Status = JobExecutionStatus.Triggered,
                FireTimeUtc = parameters.FireTimeUtc.DateTime,
                Message = string.Empty,
                FireType = "Server",
                TriggerSource = parameters.TriggerSource,
                JobScheduleConfigId = parameters.JobScheduleConfig.Id,
                ParentId = parentId,
                FireInstanceId = parameters.FireInstanceId
            };


            var isOnline = GetNodeStatus(nodeSessionId) == NodeStatus.Online;
            if (!isOnline)
            {
                jobExecutionInstance.Message = $"{nodeName} offline";
                jobExecutionInstance.Status = JobExecutionStatus.Failed;
            }
            else
            {
                switch (parameters.JobScheduleConfig.ExecutionStrategy)
                {
                    case JobExecutionStrategy.Concurrent:
                        jobExecutionInstance.Message = $"{nodeName}:triggered";
                        break;
                    case JobExecutionStrategy.WaitAny:
                        jobExecutionInstance.Status = JobExecutionStatus.Pendding;
                        jobExecutionInstance.Message = $"{nodeName}: waiting for any job";
                        break;
                    case JobExecutionStrategy.WaitAll:
                        jobExecutionInstance.Status = JobExecutionStatus.Pendding;
                        jobExecutionInstance.Message = $"{nodeName}: waiting for all job";
                        break;
                    case JobExecutionStrategy.KillAll:
                        jobExecutionInstance.Status = JobExecutionStatus.Pendding;
                        jobExecutionInstance.Message = $"{nodeName}: waiting for kill all job";
                        break;
                    default:
                        break;
                }
            }


            var jobScheduleConfigJsonString = parameters.JobScheduleConfig.ToJsonString<JobScheduleConfigModel>();

            if (parameters.NextFireTimeUtc != null)
            {
                jobExecutionInstance.NextFireTimeUtc = parameters.NextFireTimeUtc.Value.UtcDateTime;
            }
            if (parameters.PreviousFireTimeUtc != null)
            {
                jobExecutionInstance.NextFireTimeUtc = parameters.PreviousFireTimeUtc.Value.UtcDateTime;
            }
            if (parameters.ScheduledFireTimeUtc != null)
            {
                jobExecutionInstance.ScheduledFireTimeUtc = parameters.ScheduledFireTimeUtc.Value.UtcDateTime;
            }


            await dbContext.JobExecutionInstancesDbSet.AddAsync(jobExecutionInstance);

            await dbContext.SaveChangesAsync();

            return jobExecutionInstance;
        }

        public int GetNodeSessionsCount()
        {
            return _nodeSessionDict.Count;
        }

        public async ValueTask InvalidateAllNodeStatusAsync()
        {
            try
            {
                foreach (var nodeSessionId in EnumNodeSessions(NodeId.Any))
                {
                    if (GetNodeStatus(nodeSessionId) == NodeStatus.Offline)
                    {
                        ResetSession(nodeSessionId);
                        await _hearBeatBatchQueue.SendAsync(new NodeHeartBeatSessionMessage()
                        {
                            NodeSessionId = nodeSessionId
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
            }
        }

        public void SetHttpContext(NodeSessionId nodeSessionId, HttpContext? httpContext)
        {
            EnsureNodeSession(nodeSessionId).HttpContext = httpContext;
        }

        public HttpContext GetHttpContext(NodeSessionId nodeSessionId)
        {
            return EnsureNodeSession(nodeSessionId).HttpContext;
        }
    }

}
