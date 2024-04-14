using NodeService.WebServer.Data;
using NodeService.WebServer.Services.NodeSessions;

namespace NodeService.WebServer.Services.Tasks
{


    public class TaskExecutionInstanceInitializer
    {
        readonly INodeSessionService _nodeSessionService;
        readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        readonly ILogger<TaskExecutionInstanceInitializer> _logger;
        private readonly BatchQueue<JobExecutionReportMessage> _jobExecutionReportBatchQueue;
        readonly ConcurrentDictionary<string, TaskPenddingContext> _penddingContextDictionary;


        public ActionBlock<TaskPenddingContext> PenddingActionBlock { get; private set; }

        public TaskExecutionInstanceInitializer(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            ILogger<TaskExecutionInstanceInitializer> logger,
            BatchQueue<JobExecutionReportMessage> jobExecutionReportBatchQueue,
            INodeSessionService nodeSessionService)
        {
            _penddingContextDictionary = new ConcurrentDictionary<string, TaskPenddingContext>();
            _nodeSessionService = nodeSessionService;
            _dbContextFactory = dbContextFactory;
            _logger = logger;
            _jobExecutionReportBatchQueue = jobExecutionReportBatchQueue;
            PenddingActionBlock = new ActionBlock<TaskPenddingContext>(ProcessPenddingContextAsync, new ExecutionDataflowBlockOptions()
            {
                EnsureOrdered = false,
                MaxDegreeOfParallelism = Environment.ProcessorCount,

            });
        }


        private async Task ProcessPenddingContextAsync(TaskPenddingContext context)
        {
            bool canSendFireEventToNode = false;
            try
            {
                _logger.LogInformation($"{context.Id}:Start init");
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
                    _logger.LogInformation($"{context.Id}:SendJobExecutionEventAsync");
                    await _jobExecutionReportBatchQueue.SendAsync(new JobExecutionReportMessage()
                    {
                        Message = new JobExecutionReport()
                        {
                            Id = context.Id,
                            Status = JobExecutionStatus.Triggered,
                            Message = rsp.Message
                        }
                    });
                    _logger.LogInformation($"{context.Id}:SendAsync Triggered");
                    return;
                }

            }
            catch (TaskCanceledException ex)
            {
                if (ex.CancellationToken == context.CancellationToken)
                {

                }
                _logger.LogError($"{context.Id}:{ex}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
            }
            _penddingContextDictionary.TryRemove(context.Id, out _);
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
                _logger.LogInformation($"{context.Id}:SendAsync PenddingTimeout");
            }




        }


        public async ValueTask<bool> TryCancelAsync(string id)
        {
            if (_penddingContextDictionary.TryGetValue(id, out var context))
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
                    var nodeId = new NodeId(nodeInfo.Id);
                    foreach (var nodeSessionId in _nodeSessionService.EnumNodeSessions(nodeId))
                    {
                        var jobExecutionInstance = await _nodeSessionService.AddJobExecutionInstanceAsync(
                                                nodeSessionId,
                                                null,
                                                parameters);
                        var context = _penddingContextDictionary.GetOrAdd(jobExecutionInstance.Id, new TaskPenddingContext(jobExecutionInstance.Id)
                        {
                            NodeServerService = _nodeSessionService,
                            NodeSessionId = nodeSessionId,
                            FireEvent = jobExecutionInstance.ToFireEvent(parameters),
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


    }
}
