using System.Threading.Channels;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.NodeSessions;

namespace NodeService.WebServer.Services.Tasks;

public class TaskExecutionInstanceInitializer
{
    readonly ILogger<TaskExecutionInstanceInitializer> _logger;
    readonly INodeSessionService _nodeSessionService;
    readonly ApplicationRepositoryFactory<JobScheduleConfigModel> _taskDefinitionRepositoryFactory;
    private readonly ApplicationRepositoryFactory<JobExecutionInstanceModel> _taskExecutionInstanceRepositoryFactory;
    private readonly ApplicationRepositoryFactory<JobFireConfigurationModel> _taskFireRecordRepositoryFactory;
    private readonly ApplicationRepositoryFactory<JobTypeDescConfigModel> _taskTypeDescConfigRepositoryFactory;
    private readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepositoryFactory;
    readonly ConcurrentDictionary<string, TaskPenddingContext> _penddingContextDictionary;
    readonly PriorityQueue<TaskPenddingContext, TaskExecutionPriority> _priorityQueue;
    readonly BatchQueue<JobExecutionReportMessage> _taskExecutionReportBatchQueue;
    readonly ExceptionCounter _exceptionCounter;

    public TaskExecutionInstanceInitializer(
        ILogger<TaskExecutionInstanceInitializer> logger,
        ApplicationRepositoryFactory<JobScheduleConfigModel>  taskDefinitionRepositoryFactory,
        ApplicationRepositoryFactory<JobExecutionInstanceModel> taskExecutionInstanceRepositoryFactory,
        ApplicationRepositoryFactory<JobFireConfigurationModel> jobFireConfigRepositoryFactory,
        ApplicationRepositoryFactory<JobTypeDescConfigModel> taskTypeDescConfigRepositoryFactory,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepositoryFactory,
        BatchQueue<JobExecutionReportMessage> taskExecutionReportBatchQueue,
        INodeSessionService nodeSessionService,
        ExceptionCounter exceptionCounter)
    {
        _logger = logger;
        PenddingContextChannel = Channel.CreateUnbounded<TaskPenddingContext>();
        _priorityQueue = new PriorityQueue<TaskPenddingContext, TaskExecutionPriority>();
        _penddingContextDictionary = new ConcurrentDictionary<string, TaskPenddingContext>();
        _nodeSessionService = nodeSessionService;
        _taskDefinitionRepositoryFactory = taskDefinitionRepositoryFactory;
        _taskExecutionInstanceRepositoryFactory = taskExecutionInstanceRepositoryFactory;
        _taskFireRecordRepositoryFactory = jobFireConfigRepositoryFactory;
        _taskTypeDescConfigRepositoryFactory = taskTypeDescConfigRepositoryFactory;
        _nodeInfoRepositoryFactory = nodeInfoRepositoryFactory;

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

    async Task SchedulePenddingContextAsync()
    {
        while (true)
        {
            await PenddingContextChannel.Reader.WaitToReadAsync();
            while (PenddingContextChannel.Reader.TryRead(out var penddingContext))
            {
                _priorityQueue.Enqueue(penddingContext, penddingContext.FireParameters.TaskScheduleConfig.Priority);
            }
            while (PenddingActionBlock.InputCount < Environment.ProcessorCount / 2
                && 
                _priorityQueue.TryDequeue(out var penddingContext, out var _))
            {
                PenddingActionBlock.Post(penddingContext);
            }
        }
    }

    async Task ProcessPenddingContextAsync(TaskPenddingContext context)
    {
        var readyToRun = false;
        try
        {
            using var repo = _taskExecutionInstanceRepositoryFactory.CreateRepository();
            _logger.LogInformation($"{context.Id}:Start init");
            await context.EnsureInitAsync();
            switch (context.FireParameters.TaskScheduleConfig.ExecutionStrategy)
            {
                case JobExecutionStrategy.Concurrent:
                    readyToRun = true;
                    break;
                case JobExecutionStrategy.Queue:
                    readyToRun = await context.WaitForRunningTasksAsync(repo);
                    break;
                case JobExecutionStrategy.Stop:
                    readyToRun = await context.StopRunningTasksAsync(repo);
                    break;
                case JobExecutionStrategy.Skip:
                    return;
            }

            if (readyToRun)
            {
                while (!context.CancellationToken.IsCancellationRequested)
                {
                    if (context.NodeSessionService.GetNodeStatus(context.NodeSessionId) == NodeStatus.Online) break;
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
            using var taskDefinitionRepo = _taskDefinitionRepositoryFactory.CreateRepository();
            var taskDefinition = await taskDefinitionRepo.GetByIdAsync(parameters.TaskScheduleConfig.Id, cancellationToken);
            if (taskDefinition == null) return;
            if (taskDefinition.NodeList.Count == 0) return;

            using var taskFireRecordRepo = _taskFireRecordRepositoryFactory.CreateRepository();

            await taskFireRecordRepo.AddAsync(new JobFireConfigurationModel
            {
                Id = parameters.FireInstanceId,
                JobScheduleConfigJsonString = taskDefinition.ToJson()
            }, cancellationToken);

            parameters.TaskScheduleConfig = taskDefinition;

            using var taskTypeDescRepo = _taskTypeDescConfigRepositoryFactory.CreateRepository();

            taskDefinition.JobTypeDesc = await taskTypeDescRepo.GetByIdAsync(taskDefinition.JobTypeDescId, cancellationToken);


            using var nodeInfoRepo = _nodeInfoRepositoryFactory.CreateRepository();

            foreach (var node in taskDefinition.NodeList)
                try
                {
                    var nodeInfo = await nodeInfoRepo.GetByIdAsync(node.Value, cancellationToken);
                    if (nodeInfo == null) continue;
                    var nodeId = new NodeId(nodeInfo.Id);
                    foreach (var nodeSessionId in _nodeSessionService.EnumNodeSessions(nodeId))
                    {
                        var taskExecutionInstance = await AddTaskExecutionInstanceAsync(
                            nodeSessionId,
                            parameters,
                            cancellationToken);
                        var context = _penddingContextDictionary.GetOrAdd(taskExecutionInstance.Id,
                            new TaskPenddingContext(taskExecutionInstance.Id)
                            {
                                NodeSessionService = _nodeSessionService,
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

    public async Task<JobExecutionInstanceModel> AddTaskExecutionInstanceAsync(
    NodeSessionId nodeSessionId,
    FireTaskParameters parameters,
    CancellationToken cancellationToken = default)
    {
        using var taskExecutionInstanceRepo = _taskExecutionInstanceRepositoryFactory.CreateRepository();
        var nodeName = _nodeSessionService.GetNodeName(nodeSessionId);
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


        var isOnline = _nodeSessionService.GetNodeStatus(nodeSessionId) == NodeStatus.Online;
        if (isOnline)
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
        else
        {
            taskExecutionInstance.Message = $"{nodeName} offline";
            taskExecutionInstance.Status = JobExecutionStatus.Failed;
        }


        var taskScheduleConfigJsonString = parameters.TaskScheduleConfig.ToJson<JobScheduleConfigModel>();

        if (parameters.NextFireTimeUtc != null)
            taskExecutionInstance.NextFireTimeUtc = parameters.NextFireTimeUtc.Value.UtcDateTime;
        if (parameters.PreviousFireTimeUtc != null)
            taskExecutionInstance.NextFireTimeUtc = parameters.PreviousFireTimeUtc.Value.UtcDateTime;
        if (parameters.ScheduledFireTimeUtc != null)
            taskExecutionInstance.ScheduledFireTimeUtc = parameters.ScheduledFireTimeUtc.Value.UtcDateTime;


        await taskExecutionInstanceRepo.AddAsync(taskExecutionInstance, cancellationToken);

        return taskExecutionInstance;
    }

}