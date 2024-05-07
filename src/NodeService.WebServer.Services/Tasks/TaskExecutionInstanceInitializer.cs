using System.Threading.Channels;
using NodeService.WebServer.Data;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.NodeSessions;

namespace NodeService.WebServer.Services.Tasks;

public class TaskExecutionInstanceInitializer
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ILogger<TaskExecutionInstanceInitializer> _logger;
    private readonly INodeSessionService _nodeSessionService;
    private readonly ConcurrentDictionary<string, TaskPenddingContext> _penddingContextDictionary;
    private readonly PriorityQueue<TaskPenddingContext, TaskExecutionPriority> _priorityQueue;
    private readonly BatchQueue<JobExecutionReportMessage> _taskExecutionReportBatchQueue;
    private readonly ExceptionCounter _exceptionCounter;

    public TaskExecutionInstanceInitializer(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        ILogger<TaskExecutionInstanceInitializer> logger,
        BatchQueue<JobExecutionReportMessage> taskExecutionReportBatchQueue,
        INodeSessionService nodeSessionService,
        ExceptionCounter exceptionCounter)
    {
        PenddingContextChannel = Channel.CreateUnbounded<TaskPenddingContext>();
        _priorityQueue = new PriorityQueue<TaskPenddingContext, TaskExecutionPriority>();
        _penddingContextDictionary = new ConcurrentDictionary<string, TaskPenddingContext>();
        _nodeSessionService = nodeSessionService;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
        _taskExecutionReportBatchQueue = taskExecutionReportBatchQueue;
        PenddingActionBlock = new ActionBlock<TaskPenddingContext>(ProcessPenddingContextAsync,
            new ExecutionDataflowBlockOptions
            {
                EnsureOrdered = true,
                MaxDegreeOfParallelism = Environment.ProcessorCount / 2
            });
        Task.Run(SchedulePenddingContextAsync);
        _exceptionCounter = exceptionCounter;
    }

    public Channel<TaskPenddingContext> PenddingContextChannel { get; }
    public ActionBlock<TaskPenddingContext> PenddingActionBlock { get; }

    private async Task SchedulePenddingContextAsync()
    {
        while (true)
        {
            await PenddingContextChannel.Reader.WaitToReadAsync();
            while (PenddingContextChannel.Reader.TryRead(out var penddingContext))
                _priorityQueue.Enqueue(penddingContext, penddingContext.FireParameters.TaskScheduleConfig.Priority);
            {
                while (_priorityQueue.TryDequeue(out var penddingContext, out var priority))
                    PenddingActionBlock.Post(penddingContext);
            }
        }
    }

    private async Task ProcessPenddingContextAsync(TaskPenddingContext context)
    {
        var canSendFireEventToNode = false;
        try
        {
            _logger.LogInformation($"{context.Id}:Start init");
            await context.EnsureInitAsync();
            switch (context.FireParameters.TaskScheduleConfig.ExecutionStrategy)
            {
                case JobExecutionStrategy.Concurrent:
                    canSendFireEventToNode = true;
                    break;
                case JobExecutionStrategy.Queue:
                    canSendFireEventToNode = await context.WaitForRunningTasksAsync();
                    break;
                case JobExecutionStrategy.Stop:
                    canSendFireEventToNode = await context.StopRunningTasksAsync();
                    break;
                case JobExecutionStrategy.Skip:
                    return;
            }

            if (canSendFireEventToNode)
            {
                while (!context.CancellationToken.IsCancellationRequested)
                {
                    if (context.NodeServerService.GetNodeStatus(context.NodeSessionId) == NodeStatus.Online) break;
                    await Task.Delay(TimeSpan.FromSeconds(1), context.CancellationToken);
                }

                var rsp = await _nodeSessionService.SendJobExecutionEventAsync(
                    context.NodeSessionId,
                    context.FireEvent,
                    context.CancellationToken);
                _logger.LogInformation($"{context.Id}:SendJobExecutionEventAsync");
                await _taskExecutionReportBatchQueue.SendAsync(new JobExecutionReportMessage
                {
                    Message = new JobExecutionReport
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
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError($"{context.Id}:{ex}");
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }

        _penddingContextDictionary.TryRemove(context.Id, out _);
        if (context.CancellationToken.IsCancellationRequested)
        {
            await _taskExecutionReportBatchQueue.SendAsync(new JobExecutionReportMessage
            {
                Message = new JobExecutionReport
                {
                    Id = context.Id,
                    Status = JobExecutionStatus.PenddingTimeout
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


    public async Task InitAsync(FireTaskParameters parameters, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var taskScheduleConfig = await dbContext.JobScheduleConfigurationDbSet.FirstOrDefaultAsync(
                x => x.Id == parameters.TaskScheduleConfig.Id,
                cancellationToken);
            if (taskScheduleConfig == null) return;
            if (taskScheduleConfig.NodeList.Count == 0) return;
            await dbContext.JobFireConfigurationsDbSet.AddAsync(new JobFireConfigurationModel
            {
                Id = parameters.FireInstanceId,
                JobScheduleConfigJsonString = taskScheduleConfig.ToJson()
            }, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            parameters.TaskScheduleConfig = taskScheduleConfig;
            taskScheduleConfig.JobTypeDesc = await dbContext.JobTypeDescConfigurationDbSet.FirstOrDefaultAsync(
                x => x.Id == taskScheduleConfig.JobTypeDescId,
                cancellationToken);
            foreach (var node in taskScheduleConfig.NodeList)
                try
                {
                    var nodeInfo = await dbContext.NodeInfoDbSet.FirstOrDefaultAsync(
                        x => x.Id == node.Value,
                        cancellationToken);
                    if (nodeInfo == null) continue;
                    var nodeId = new NodeId(nodeInfo.Id);
                    foreach (var nodeSessionId in _nodeSessionService.EnumNodeSessions(nodeId))
                    {
                        var taskExecutionInstance = await _nodeSessionService.AddJobExecutionInstanceAsync(
                            nodeSessionId,
                            parameters,
                            cancellationToken);
                        var context = _penddingContextDictionary.GetOrAdd(taskExecutionInstance.Id,
                            new TaskPenddingContext(taskExecutionInstance.Id)
                            {
                                NodeServerService = _nodeSessionService,
                                NodeSessionId = nodeSessionId,
                                FireEvent = taskExecutionInstance.ToFireEvent(parameters),
                                FireParameters = parameters
                            });

                        await PenddingContextChannel.Writer.WriteAsync(context, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }

            _logger.LogInformation($"Job initialized {parameters.FireInstanceId}");
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }

    }
}