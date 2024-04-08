using NodeService.WebServer.Data;

namespace NodeService.WebServer.Services.JobSchedule
{


    public class JobExecutionInstanceInitializer
    {
        readonly INodeSessionService _nodeSessionService;
        readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        readonly ILogger<JobExecutionInstanceInitializer> _logger;
        readonly ConcurrentDictionary<string, PenddingContext> _penddingContextDictionary;


        public ActionBlock<PenddingContext> PenddingActionBlock { get; private set; }

        public JobExecutionInstanceInitializer(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            ILogger<JobExecutionInstanceInitializer> logger,
            INodeSessionService nodeSessionService)
        {
            _penddingContextDictionary = new ConcurrentDictionary<string, PenddingContext>();
            _nodeSessionService = nodeSessionService;
            _dbContextFactory = dbContextFactory;
            _logger = logger;
            PenddingActionBlock = new ActionBlock<PenddingContext>(ProcessPenddingContextAsync);
        }


        private async Task ProcessPenddingContextAsync(PenddingContext context)
        {
            bool canSendEventToNode = false;
            try
            {
                await context.InitAsync();
                switch (context.FireParameters.JobScheduleConfig.ExecutionStrategy)
                {
                    case JobExecutionStrategy.Concurrent:
                        canSendEventToNode = true;
                        break;
                    case JobExecutionStrategy.WaitAny:
                        canSendEventToNode = await context.WaitAnyJobTerminatedAsync();
                        break;
                    case JobExecutionStrategy.WaitAll:
                        canSendEventToNode = await context.WaitAllJobTerminatedAsync();
                        break;
                    case JobExecutionStrategy.KillAll:
                        canSendEventToNode = await context.KillAllJobAsync();
                        break;
                    default:
                        break;
                }

                if (canSendEventToNode)
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
                        context.JobExecutionInstance.ToTriggerEvent(),
                        context.CancellationToken);
                    await _nodeSessionService.BatchUpdateJobExecutionInstanceAsync(new UpdateJobExecutionInstanceParameters()
                    {
                        Id = context.JobExecutionInstance.Id,
                        Message = rsp.Message
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
                await _nodeSessionService.BatchUpdateJobExecutionInstanceAsync(new UpdateJobExecutionInstanceParameters()
                {
                    Id = context.JobExecutionInstance.Id,
                    Status = JobExecutionStatus.PenddingTimeout,
                });
            }




        }


        public async ValueTask CancelAsync(JobExecutionInstanceModel jobExecutionInstance)
        {
            if (this._penddingContextDictionary.TryGetValue(jobExecutionInstance.Id,out var context))
            {
               await context.CancelAsync();
            }
        }


        public async Task InitAsync(JobFireParameters jobFireParameters)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var jobScheduleConfig = await dbContext.JobScheduleConfigurationDbSet.FindAsync(jobFireParameters.JobScheduleConfig.Id);
            if (jobScheduleConfig == null)
            {
                return;
            }
            jobFireParameters.JobScheduleConfig = jobScheduleConfig;
            jobScheduleConfig.JobTypeDesc = await dbContext.JobTypeDescConfigurationDbSet.FirstOrDefaultAsync(x => x.Id == jobScheduleConfig.JobTypeDescId);
            foreach (var node in jobScheduleConfig.NodeList)
            {
                try
                {
                    var nodeInfo = await dbContext.NodeInfoDbSet.FindAsync(node.Value);

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
                                                jobFireParameters);
                        var context = this._penddingContextDictionary.GetOrAdd(jobExecutionInstance.Id, new PenddingContext(jobExecutionInstance.Id, _nodeSessionService)
                        {
                            NodeSessionId = nodeSessionId,
                            Event = jobExecutionInstance.ToTriggerEvent(),
                            FireParameters = jobFireParameters,
                            JobExecutionInstance = jobExecutionInstance,
                        });
                        PenddingActionBlock.Post(context);
                    }

                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.ToString());
                }

            }
            if (jobScheduleConfig.NodeList.Count > 0)
            {
                await dbContext.JobFireConfigurationsDbSet.AddAsync(new JobFireConfigurationModel()
                {
                    Id = jobFireParameters.FireInstanceId,
                    JobScheduleConfigJsonString = jobScheduleConfig.ToJson(),
                });
            }
            await dbContext.SaveChangesAsync();
            _logger.LogInformation($"Job initialized {jobFireParameters.FireInstanceId}");
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
