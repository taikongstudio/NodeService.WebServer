using NodeService.WebServer.Data;
using NodeService.WebServer.Services.NodeSessions;
using System.Collections.Immutable;

namespace NodeService.WebServer.Services.JobSchedule
{


    public class JobExecutionInstanceInitializer
    {
        readonly INodeSessionService _nodeSessionService;
        readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        readonly ILogger<JobExecutionInstanceInitializer> _logger;
        private readonly BatchQueue<JobExecutionReportMessage> _jobExecutionReportBatchQueue;
        readonly ConcurrentDictionary<string, PenddingContext> _penddingContextDictionary;


        public ActionBlock<PenddingContext> PenddingActionBlock { get; private set; }

        public JobExecutionInstanceInitializer(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            ILogger<JobExecutionInstanceInitializer> logger,
            BatchQueue<JobExecutionReportMessage> jobExecutionReportBatchQueue,
            INodeSessionService nodeSessionService)
        {
            _penddingContextDictionary = new ConcurrentDictionary<string, PenddingContext>();
            _nodeSessionService = nodeSessionService;
            _dbContextFactory = dbContextFactory;
            _logger = logger;
            _jobExecutionReportBatchQueue = jobExecutionReportBatchQueue;
            PenddingActionBlock = new ActionBlock<PenddingContext>(ProcessPenddingContextAsync);
        }


        private async Task ProcessPenddingContextAsync(PenddingContext context)
        {
            bool canSendFireEventToNode = false;
            try
            {
                await context.InitAsync();
                switch (context.FireParameters.JobScheduleConfig.ExecutionStrategy)
                {
                    case JobExecutionStrategy.Concurrent:
                        canSendFireEventToNode = true;
                        break;
                    case JobExecutionStrategy.WaitAny:
                        canSendFireEventToNode = await context.WaitAnyJobTerminatedAsync();
                        break;
                    case JobExecutionStrategy.WaitAll:
                        canSendFireEventToNode = await context.WaitAllJobTerminatedAsync();
                        break;
                    case JobExecutionStrategy.KillAll:
                        canSendFireEventToNode = await context.KillAllJobAsync();
                        break;
                    default:
                        break;
                }

                if (canSendFireEventToNode)
                {
                    while (!context.CancellationToken.IsCancellationRequested)
                    {
                        if (context.NodeServerService.GetNodeStatus(context.NodeSessionId) == NodeStatus.Online)
                        {
                            break;
                        }
                        await Task.Delay(TimeSpan.FromSeconds(1), context.CancellationToken);
                    }
                    var rsp = await _nodeSessionService.SendJobExecutionEventAsync(
                        context.NodeSessionId,
                        context.FireEvent,
                        context.CancellationToken);

                    await _jobExecutionReportBatchQueue.SendAsync(new JobExecutionReportMessage()
                    {
                        Message = new JobExecutionReport()
                        {
                            Id = context.Id,
                            Status = JobExecutionStatus.Triggered,
                            Message = rsp.Message
                        }
                    });
                    return;
                }

            }
            catch (TaskCanceledException ex)
            {
                if (ex.CancellationToken == context.CancellationToken)
                {

                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
            }
            this._penddingContextDictionary.TryRemove(context.Id, out _);
            if (context.CancellationToken.IsCancellationRequested)
            {
                await _jobExecutionReportBatchQueue.SendAsync(new JobExecutionReportMessage()
                {
                    Message = new JobExecutionReport()
                    {
                        Id = context.Id,
                        Status = JobExecutionStatus.PenddingTimeout,
                    }
                });
            }




        }


        public async ValueTask<bool> TryCancelAsync(string id)
        {
            if (this._penddingContextDictionary.TryGetValue(id, out var context))
            {
                await context.CancelAsync();
                return true;
            }
            return false;
        }


        public async Task InitAsync(JobFireParameters parameters)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var jobScheduleConfig = await dbContext.JobScheduleConfigurationDbSet.FirstOrDefaultAsync(
                x => x.Id == parameters.JobScheduleConfig.Id);
            if (jobScheduleConfig == null)
            {
                return;
            }
            if (jobScheduleConfig.NodeList.Count == 0)
            {
                return;
            }
            await dbContext.JobFireConfigurationsDbSet.AddAsync(new JobFireConfigurationModel()
            {
                Id = parameters.FireInstanceId,
                JobScheduleConfigJsonString = jobScheduleConfig.ToJson(),
            });
            await dbContext.SaveChangesAsync();
            parameters.JobScheduleConfig = jobScheduleConfig;
            jobScheduleConfig.JobTypeDesc = await dbContext.JobTypeDescConfigurationDbSet.FirstOrDefaultAsync(
                x => x.Id == jobScheduleConfig.JobTypeDescId);
            foreach (var node in jobScheduleConfig.NodeList)
            {
                try
                {
                    var nodeInfo = await dbContext.NodeInfoDbSet.FirstOrDefaultAsync(x => x.Id == node.Value);
                    if (nodeInfo == null)
                    {
                        continue;
                    }
                    bool isFilteredIn = FilterNode(jobScheduleConfig, nodeInfo);

                    if (!isFilteredIn)
                    {
                        continue;
                    }
                    var nodeId = new NodeId(nodeInfo.Id);
                    foreach (var nodeSessionId in _nodeSessionService.EnumNodeSessions(nodeId))
                    {
                        var jobExecutionInstance = await _nodeSessionService.AddJobExecutionInstanceAsync(
                                                nodeSessionId,
                                                null,
                                                parameters);
                        var context = this._penddingContextDictionary.GetOrAdd(jobExecutionInstance.Id, new PenddingContext(jobExecutionInstance.Id)
                        {
                            NodeServerService = _nodeSessionService,
                            NodeSessionId = nodeSessionId,
                            FireEvent = jobExecutionInstance.ToFireEvent(parameters, jobExecutionInstance.Id, jobExecutionInstance.FireInstanceId),
                            FireParameters = parameters,
                        });
                        PenddingActionBlock.Post(context);
                    }

                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.ToString());
                }

            }

            _logger.LogInformation($"Job initialized {parameters.FireInstanceId}");
        }



        private bool FilterNode(JobScheduleConfigModel jobScheduleConfig, NodeInfoModel nodeInfoModel)
        {
            switch (jobScheduleConfig.DnsFilterType)
            {
                case "include":
                    return DnsFilterIncludeNode(jobScheduleConfig, nodeInfoModel);
                case "exclude":
                    return DnsFilterExcludeNode(jobScheduleConfig, nodeInfoModel);
                default:
                    return false;
            }
        }

        private bool DnsFilterIncludeNode(JobScheduleConfigModel jobScheduleConfig, NodeInfoModel nodeInfo)
        {
            return jobScheduleConfig.DnsFilters.Any(x => x.Value == nodeInfo.Name);
        }

        private bool DnsFilterExcludeNode(JobScheduleConfigModel jobScheduleConfig, NodeInfoModel nodeInfo)
        {
            return !jobScheduleConfig.DnsFilters.Any(x => x.Value == nodeInfo.Name);
        }

        private bool NoFilter(NodeInfoModel nodeInfo)
        {
            return true;
        }

    }
}
