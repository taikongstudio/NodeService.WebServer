﻿using System.ComponentModel.DataAnnotations;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.NodeSessions;

namespace NodeService.WebServer.Services.Tasks;

public class TaskFireService : BackgroundService
{
    readonly ExceptionCounter _exceptionCounter;
    readonly BatchQueue<FireTaskParameters> _fireTaskBatchQueue;
    readonly ILogger<TaskFireService> _logger;
    readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepositoryFactory;
    readonly INodeSessionService _nodeSessionService;
    readonly PriorityQueue<TaskPenddingContext, TaskExecutionPriority> _priorityQueue;
    readonly BatchQueue<TaskCancellationParameters> _taskCancellationQueue;
    readonly ITaskPenddingContextManager _taskPenddingContextManager;
    readonly ApplicationRepositoryFactory<JobScheduleConfigModel> _taskDefinitionRepositoryFactory;
    readonly ApplicationRepositoryFactory<JobExecutionInstanceModel> _taskExecutionInstanceRepositoryFactory;
    readonly BatchQueue<JobExecutionReportMessage> _taskExecutionReportBatchQueue;
    readonly ApplicationRepositoryFactory<JobFireConfigurationModel> _taskFireRecordRepositoryFactory;
    readonly ApplicationRepositoryFactory<JobTypeDescConfigModel> _taskTypeDescConfigRepositoryFactory;

    public TaskFireService(
        ILogger<TaskFireService> logger,
        ApplicationRepositoryFactory<JobScheduleConfigModel> taskDefinitionRepositoryFactory,
        ApplicationRepositoryFactory<JobExecutionInstanceModel> taskExecutionInstanceRepositoryFactory,
        ApplicationRepositoryFactory<JobFireConfigurationModel> jobFireConfigRepositoryFactory,
        ApplicationRepositoryFactory<JobTypeDescConfigModel> taskTypeDescConfigRepositoryFactory,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepositoryFactory,
        BatchQueue<JobExecutionReportMessage> taskExecutionReportBatchQueue,
        INodeSessionService nodeSessionService,
        ExceptionCounter exceptionCounter,
        BatchQueue<FireTaskParameters> fireTaskBatchQueue,
        BatchQueue<TaskCancellationParameters> taskCancellationQueue,
        ITaskPenddingContextManager taskPenddingContextManager)
    {
        _logger = logger;
        PenddingContextChannel = Channel.CreateUnbounded<TaskPenddingContext>();
        _priorityQueue = new PriorityQueue<TaskPenddingContext, TaskExecutionPriority>();
        _nodeSessionService = nodeSessionService;
        _taskDefinitionRepositoryFactory = taskDefinitionRepositoryFactory;
        _taskExecutionInstanceRepositoryFactory = taskExecutionInstanceRepositoryFactory;
        _taskFireRecordRepositoryFactory = jobFireConfigRepositoryFactory;
        _taskTypeDescConfigRepositoryFactory = taskTypeDescConfigRepositoryFactory;
        _nodeInfoRepositoryFactory = nodeInfoRepositoryFactory;

        _taskExecutionReportBatchQueue = taskExecutionReportBatchQueue;
        _exceptionCounter = exceptionCounter;
        _fireTaskBatchQueue = fireTaskBatchQueue;
        _taskCancellationQueue = taskCancellationQueue;
        _taskPenddingContextManager = taskPenddingContextManager;
        PenddingActionBlock = new ActionBlock<TaskPenddingContext>(ProcessPenddingContextAsync,
        new ExecutionDataflowBlockOptions
        {
            EnsureOrdered = true,
            MaxDegreeOfParallelism = Environment.ProcessorCount / 2
        });
    }

    public Channel<TaskPenddingContext> PenddingContextChannel { get; }
    public ActionBlock<TaskPenddingContext> PenddingActionBlock { get; }

    private async Task SchedulePenddingContextAsync(object? state)
    {
        if (state is not CancellationToken cancellationToken) return;
        while (true)
        {
            await PenddingContextChannel.Reader.WaitToReadAsync(cancellationToken);
            while (PenddingContextChannel.Reader.TryRead(out var penddingContext))
                _priorityQueue.Enqueue(penddingContext, penddingContext.TaskDefinition.Priority);
            while (_priorityQueue.Count > 0)
            {
                while (PenddingActionBlock.InputCount < 128
                   &&
                   _priorityQueue.TryDequeue(out var penddingContext, out var _))
                {
                    PenddingActionBlock.Post(penddingContext);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
            }
        }
    }

    private async Task ProcessPenddingContextAsync(TaskPenddingContext context)
    {
        var readyToRun = false;
        try
        {
            using var repo = _taskExecutionInstanceRepositoryFactory.CreateRepository();
            _logger.LogInformation($"{context.Id}:Start init");
            await context.EnsureInitAsync();
            switch (context.TaskDefinition.ExecutionStrategy)
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

                var rsp = await _nodeSessionService.SendTaskExecutionEventAsync(
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
        catch (OperationCanceledException ex)
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

        _taskPenddingContextManager.RemoveContext(context.Id, out _);
        if (context.CancellationToken.IsCancellationRequested)
        {
            await _taskExecutionReportBatchQueue.SendAsync(new JobExecutionReportMessage
            {
                Message = new JobExecutionReport
                {
                    Id = context.Id,
                    Status = JobExecutionStatus.PenddingTimeout,
                    Message = "Timeout"
                }
            });
            _logger.LogInformation($"{context.Id}:SendAsync PenddingTimeout");
        }
    }

    private async Task FireTaskAsync(
        IRepository<JobScheduleConfigModel> taskDefinitionRepo,
        IRepository<JobExecutionInstanceModel> taskExecutionInstanceRepo,
        IRepository<JobFireConfigurationModel> taskFireRecordRepo,
        IRepository<JobTypeDescConfigModel> taskTypeDescConfigRepo,
        IRepository<NodeInfoModel> nodeInfoRepo,
        FireTaskParameters parameters,
        JobScheduleConfigModel taskDefinition,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var nodeIdListFilter = DataFilterCollection<string>.Includes(taskDefinition.NodeList.Select(x => x.Value));
            var nodeInfoList = await nodeInfoRepo.ListAsync(
                new NodeInfoSpecification(
                    AreaTags.Any,
                    NodeStatus.All,
                    nodeIdListFilter),
                cancellationToken);
            foreach (var nodeEntry in taskDefinition.NodeList)
                try
                {
                    if (string.IsNullOrEmpty(nodeEntry.Value))
                    {
                        continue;
                    }
                    var nodeInfo = nodeInfoList.FirstOrDefault(x => x.Id == nodeEntry.Value);
                    if (nodeInfo == null) continue;
                    var nodeId = new NodeId(nodeInfo.Id);
                    foreach (var nodeSessionId in _nodeSessionService.EnumNodeSessions(nodeId))
                    {
                        var taskExecutionInstance = await AddTaskExecutionInstanceAsync(
                            taskDefinition,
                            taskExecutionInstanceRepo,
                            nodeSessionId,
                            parameters,
                            cancellationToken);
                        var context = new TaskPenddingContext(taskExecutionInstance.Id)
                        {
                            NodeSessionService = _nodeSessionService,
                            NodeSessionId = nodeSessionId,
                            FireEvent = taskExecutionInstance.ToFireEvent(taskDefinition, parameters.EnvironmentVariables),
                            FireParameters = parameters,
                            TaskDefinition = taskDefinition
                        };

                        if (_taskPenddingContextManager.AddContext(context))
                        {
                            await PenddingContextChannel.Writer.WriteAsync(context, cancellationToken);
                        }

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
        JobScheduleConfigModel taskDefinition,
        IRepository<JobExecutionInstanceModel> taskExecutionInstanceRepo,
        NodeSessionId nodeSessionId,
        FireTaskParameters parameters,
        CancellationToken cancellationToken = default)
    {
        var nodeName = _nodeSessionService.GetNodeName(nodeSessionId);
        var taskExecutionInstance = new JobExecutionInstanceModel
        {
            Id = Guid.NewGuid().ToString(),
            Name = $"{nodeName} {taskDefinition.Name} {parameters.FireInstanceId}",
            NodeInfoId = nodeSessionId.NodeId.Value,
            Status = JobExecutionStatus.Triggered,
            FireTimeUtc = parameters.FireTimeUtc.DateTime,
            Message = string.Empty,
            FireType = "Server",
            TriggerSource = parameters.TriggerSource,
            JobScheduleConfigId = taskDefinition.Id,
            ParentId = parameters.ParentTaskId,
            FireInstanceId = parameters.FireInstanceId
        };


        var isOnline = _nodeSessionService.GetNodeStatus(nodeSessionId) == NodeStatus.Online;
        if (isOnline)
        {
            switch (taskDefinition.ExecutionStrategy)
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

        if (parameters.NextFireTimeUtc != null)
            taskExecutionInstance.NextFireTimeUtc = parameters.NextFireTimeUtc.Value.UtcDateTime;
        if (parameters.PreviousFireTimeUtc != null)
            taskExecutionInstance.NextFireTimeUtc = parameters.PreviousFireTimeUtc.Value.UtcDateTime;
        if (parameters.ScheduledFireTimeUtc != null)
            taskExecutionInstance.ScheduledFireTimeUtc = parameters.ScheduledFireTimeUtc.Value.UtcDateTime;


        await taskExecutionInstanceRepo.AddAsync(taskExecutionInstance, cancellationToken);

        return taskExecutionInstance;
    }

    private (string TaskDefinitionId, string TaskFireInstanceId) TaskParametersGroupFunc(
        FireTaskParameters fireTaskParameters)
    {
        return (fireTaskParameters.TaskDefinitionId, fireTaskParameters.FireInstanceId);
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _ = Task.Factory.StartNew(SchedulePenddingContextAsync, cancellationToken, cancellationToken);
        await foreach (var arrayPoolCollection in _fireTaskBatchQueue.ReceiveAllAsync(cancellationToken))
            try
            {
                using var taskExecutionInstanceRepo = _taskExecutionInstanceRepositoryFactory.CreateRepository();
                using var taskFireRecordRepo = _taskFireRecordRepositoryFactory.CreateRepository();
                using var taskTypeDescRepo = _taskTypeDescConfigRepositoryFactory.CreateRepository();
                using var taskDefinitionRepo = _taskDefinitionRepositoryFactory.CreateRepository();
                using var nodeInfoRepo = _nodeInfoRepositoryFactory.CreateRepository();
                foreach (var fireTaskParametersGroup in arrayPoolCollection.Where(static x => x != default)
                             .GroupBy(TaskParametersGroupFunc))
                {
                    var (taskDefinitionId, fireTaskInstanceId) = fireTaskParametersGroup.Key;

                    var taskDefinition = await taskDefinitionRepo.GetByIdAsync(
                        taskDefinitionId,
                        cancellationToken);

                    if (taskDefinition == null || string.IsNullOrEmpty(taskDefinition.JobTypeDescId)) continue;
                    if (taskDefinition.NodeList.Count == 0) continue;

                    await taskFireRecordRepo.AddAsync(new JobFireConfigurationModel
                    {
                        Id = fireTaskInstanceId,
                        JobScheduleConfigJsonString = taskDefinition.ToJson()
                    }, cancellationToken);


                    taskDefinition.JobTypeDesc = await taskTypeDescRepo.GetByIdAsync(
                        taskDefinition.JobTypeDescId,
                        cancellationToken);
                    foreach (var fireTaskParameters in fireTaskParametersGroup)
                    {
                        if (fireTaskParameters.NodeList != null && fireTaskParameters.NodeList.Count > 0)
                            taskDefinition.NodeList = fireTaskParameters.NodeList;
                        await FireTaskAsync(
                            taskDefinitionRepo,
                            taskExecutionInstanceRepo,
                            taskFireRecordRepo,
                            taskTypeDescRepo,
                            nodeInfoRepo,
                            fireTaskParameters,
                            taskDefinition,
                            cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
            finally
            {

            }
    }
}