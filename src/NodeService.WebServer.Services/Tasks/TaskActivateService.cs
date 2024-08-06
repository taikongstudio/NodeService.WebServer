﻿using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.TaskSchedule;
using System.Net;

namespace NodeService.WebServer.Services.Tasks;

public class TaskActivateService : BackgroundService
{
    readonly ILogger<TaskActivateService> _logger;
    readonly INodeSessionService _nodeSessionService;
    readonly ITaskPenddingContextManager _taskPenddingContextManager;
    readonly TaskFlowExecutor _taskFlowExecutor;
    readonly NodeInfoQueryService _nodeInfoQueryService;
    readonly ConfigurationQueryService _configurationQueryService;
    readonly ExceptionCounter _exceptionCounter;
    readonly PriorityQueue<TaskPenddingContext, TaskExecutionPriority> _priorityQueue;
    readonly BatchQueue<TaskCancellationParameters> _taskCancellationQueue;
    readonly IAsyncQueue<TaskExecutionReport> _taskExecutionReportBatchQueue;
    readonly BatchQueue<TaskActivateServiceParameters> _serviceParametersBatchQueue;
    readonly ApplicationRepositoryFactory<TaskDefinitionModel> _taskDefinitionRepositoryFactory;
    readonly ApplicationRepositoryFactory<TaskExecutionInstanceModel> _taskExecutionInstanceRepositoryFactory;
    readonly ApplicationRepositoryFactory<TaskActivationRecordModel> _taskActivationRecordRepoFactory;
    readonly ApplicationRepositoryFactory<TaskTypeDescConfigModel> _taskTypeDescConfigRepositoryFactory;
    readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepositoryFactory;
    readonly ApplicationRepositoryFactory<TaskFlowExecutionInstanceModel> _taskFlowExecutionInstanceRepoFactory;
    readonly ApplicationRepositoryFactory<TaskFlowTemplateModel> _taskFlowTemplateRepoFactory;
    readonly TaskActivationRecordExecutor _taskActivationRecordExecutor;
    readonly IDelayMessageBroadcast _delayMessageBroadcast;
    readonly IAsyncQueue<KafkaDelayMessage> _delayMessageQueue;
    readonly KafkaOptions _kafkaOptions;

    private const string SubType_PendingContextCheck = "PendingContextCheck";

    public TaskActivateService(
        ILogger<TaskActivateService> logger,
        INodeSessionService nodeSessionService,
        ExceptionCounter exceptionCounter,
        ApplicationRepositoryFactory<TaskDefinitionModel> taskDefinitionRepositoryFactory,
        ApplicationRepositoryFactory<TaskExecutionInstanceModel> taskExecutionInstanceRepositoryFactory,
        ApplicationRepositoryFactory<TaskActivationRecordModel> taskActivationRepositoryFactory,
        ApplicationRepositoryFactory<TaskTypeDescConfigModel> taskTypeDescConfigRepositoryFactory,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepositoryFactory,
        ApplicationRepositoryFactory<TaskFlowTemplateModel> taskFlowTemplateRepoFactory,
        ApplicationRepositoryFactory<TaskFlowExecutionInstanceModel> taskFlowExecutionInstanceRepoFactory,
        IAsyncQueue<TaskExecutionReport> taskExecutionReportBatchQueue,
        BatchQueue<TaskActivateServiceParameters> serviceParametersBatchQueue,
        BatchQueue<TaskCancellationParameters> taskCancellationQueue,
        ITaskPenddingContextManager taskPenddingContextManager,
        TaskFlowExecutor taskFlowExecutor,
        NodeInfoQueryService nodeInfoQueryService,
        ConfigurationQueryService configurationQueryService,
        TaskActivationRecordExecutor taskActivationRecordExecutor,
        IDelayMessageBroadcast delayMessageBroadcast,
        IAsyncQueue<KafkaDelayMessage> delayMessageQueue)
    {
        _logger = logger;
        _priorityQueue = new PriorityQueue<TaskPenddingContext, TaskExecutionPriority>();
        _nodeSessionService = nodeSessionService;
        _taskDefinitionRepositoryFactory = taskDefinitionRepositoryFactory;
        _taskExecutionInstanceRepositoryFactory = taskExecutionInstanceRepositoryFactory;
        _taskActivationRecordRepoFactory = taskActivationRepositoryFactory;
        _taskTypeDescConfigRepositoryFactory = taskTypeDescConfigRepositoryFactory;
        _nodeInfoRepositoryFactory = nodeInfoRepositoryFactory;
        _taskFlowExecutionInstanceRepoFactory = taskFlowExecutionInstanceRepoFactory;
        _taskFlowTemplateRepoFactory = taskFlowTemplateRepoFactory;
        _taskExecutionReportBatchQueue = taskExecutionReportBatchQueue;
        _exceptionCounter = exceptionCounter;
        _serviceParametersBatchQueue = serviceParametersBatchQueue;
        _taskCancellationQueue = taskCancellationQueue;
        _taskPenddingContextManager = taskPenddingContextManager;
        _taskFlowExecutor = taskFlowExecutor;
        _nodeInfoQueryService = nodeInfoQueryService;
        _configurationQueryService = configurationQueryService;
        _taskActivationRecordExecutor = taskActivationRecordExecutor;
        _delayMessageBroadcast = delayMessageBroadcast;
        _delayMessageQueue = delayMessageQueue;
        _delayMessageBroadcast.AddHandler(nameof(TaskActivateService), ProcessDelayMessage);
    }

    private async ValueTask ProcessDelayMessage(KafkaDelayMessage kafkaDelayMessage,CancellationToken cancellationToken=default)
    {
        switch (kafkaDelayMessage.SubType)
        {
            case SubType_PendingContextCheck:
                await ProcessPenddingContextAsync(kafkaDelayMessage, cancellationToken);
                break;
            default:
                break;
        }


    }

    public async Task<bool> StopRunningTasksAsync(
        string nodeId,
        string taskDefinitionId,
        IRepository<TaskExecutionInstanceModel> repository,
        BatchQueue<TaskCancellationParameters> taskCancellationQueue, CancellationToken cancellationToken = default)
    {
        var queryResult = await QueryTaskExecutionInstancesAsync(repository, new QueryTaskExecutionInstanceListParameters
            {
                NodeIdList = [nodeId],
                TaskDefinitionIdList = [taskDefinitionId],
                Status = TaskExecutionStatus.Running
            }, cancellationToken);

        if (queryResult.IsEmpty) return true;
        foreach (var taskExecutionInstance in queryResult.Items)
        {
            await taskCancellationQueue.SendAsync(new TaskCancellationParameters(taskExecutionInstance.Id, nameof(TaskPenddingContext), Dns.GetHostName()), cancellationToken);
        }
        return true;
    }

    public async Task<bool> QueryRunningTasksAsync(
        string nodeId,
        string taskDefinitionId,
        IRepository<TaskExecutionInstanceModel> repository,
        CancellationToken cancellationToken = default)
    {
        var queryResult = await QueryTaskExecutionInstancesAsync(repository,
            new QueryTaskExecutionInstanceListParameters
            {
                NodeIdList = [nodeId],
                TaskDefinitionIdList = [taskDefinitionId],
                Status = TaskExecutionStatus.Running
            }, cancellationToken);
        return queryResult.HasValue;
    }


    public async Task<ListQueryResult<TaskExecutionInstanceModel>> QueryTaskExecutionInstancesAsync(
        IRepository<TaskExecutionInstanceModel> repository,
        QueryTaskExecutionInstanceListParameters queryParameters,
        CancellationToken cancellationToken = default)
    {
        var queryResult = await repository.PaginationQueryAsync(new TaskExecutionInstanceListSpecification(
                queryParameters.Keywords,
                queryParameters.Status,
                queryParameters.NodeIdList,
                queryParameters.TaskDefinitionIdList,
                queryParameters.TaskExecutionInstanceIdList,
                queryParameters.SortDescriptions),
            cancellationToken: cancellationToken);
        return queryResult;
    }

    async ValueTask<TaskActivationRecordModel?> QueryTaskActiveRecordAsync(
    string fireInstanceId,
    CancellationToken cancellationToken = default)
    {
        await using var taskActiveRecordRepo = await _taskActivationRecordRepoFactory.CreateRepositoryAsync(cancellationToken);
        var taskActiveRecord = await taskActiveRecordRepo.GetByIdAsync(fireInstanceId, cancellationToken);
        return taskActiveRecord;
    }

    async ValueTask ProcessPenddingContextAsync(KafkaDelayMessage delayMessage, CancellationToken cancellationToken = default)
    {
        var readyToRun = false;
        var penddingLimitTimeSeconds = 0;


        try
        {
            var timeSpan = delayMessage.ScheduleDateTime - DateTime.UtcNow;

            var taskExecutionInstanceId = delayMessage.Id;
            if (taskExecutionInstanceId == null)
            {
                return;
            }

            await using var taskExecutionInstanceRepo = await _taskExecutionInstanceRepositoryFactory.CreateRepositoryAsync(cancellationToken);
            var taskExecutionInstance = await taskExecutionInstanceRepo.GetByIdAsync(taskExecutionInstanceId, cancellationToken);
            if (taskExecutionInstance == null)
            {
                return;
            }
            if (taskExecutionInstance.Status > TaskExecutionStatus.Triggered)
            {
                delayMessage.Handled = true;
                return;
            }
            if (timeSpan < TimeSpan.Zero)
            {
                timeSpan = TimeSpan.Zero;
            }
            using var cancellationTokenSource = new CancellationTokenSource(timeSpan);
            cancellationToken = cancellationTokenSource.Token;
            var taskActivationRecordId = taskExecutionInstance.FireInstanceId;
            var nodeSessionId = new NodeSessionId(taskExecutionInstance.NodeInfoId);
            var taskActivationRecord = await QueryTaskActiveRecordAsync(taskActivationRecordId, cancellationToken);
            if (taskActivationRecord == null)
            {
                return;
            }
            var taskDefinition = taskActivationRecord.GetTaskDefinition();
            if (taskDefinition == null)
            {
                return;
            }
            var nodeStatus = _nodeSessionService.GetNodeStatus(nodeSessionId);
            if (nodeStatus != NodeStatus.Online)
            {
                bool isPenddingTimeOut = false;
                timeSpan = DateTime.UtcNow - taskExecutionInstance.FireTimeUtc;
                if (taskDefinition.PenddingLimitTimeSeconds == 0)
                {
                    isPenddingTimeOut = true;
                }
                else if (taskDefinition.PenddingLimitTimeSeconds > 0
                    &&
                     timeSpan > TimeSpan.FromSeconds(taskDefinition.PenddingLimitTimeSeconds))
                {
                    isPenddingTimeOut = true;
                }

                if (isPenddingTimeOut)
                {
                    _taskPenddingContextManager.RemoveContext(delayMessage.Id, out _);
                    await SendTaskExecutionReportAsync(
                            delayMessage.Id,
                            TaskExecutionStatus.PenddingTimeout,
                            $"Time out after waiting {taskDefinition.PenddingLimitTimeSeconds} seconds");
                    _logger.LogInformation($"{delayMessage.Id}:SendAsync PenddingTimeout");
                    delayMessage.Handled = true;
                }
                return;
            }

            penddingLimitTimeSeconds = taskDefinition.PenddingLimitTimeSeconds;
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            _logger.LogInformation($"{delayMessage.Id}:Start init");
            switch (taskDefinition.ExecutionStrategy)
            {
                case TaskExecutionStrategy.Concurrent:
                    readyToRun = true;
                    break;
                case TaskExecutionStrategy.Queue:
                    readyToRun = await QueryRunningTasksAsync(
                        nodeSessionId.NodeId.Value,
                        taskActivationRecord.TaskDefinitionId,
                        taskExecutionInstanceRepo,
                        cancellationToken);
                    break;
                case TaskExecutionStrategy.Stop:
                    readyToRun = await StopRunningTasksAsync(
                        nodeSessionId.NodeId.Value,
                        taskActivationRecord.TaskDefinitionId,
                        taskExecutionInstanceRepo,
                        _taskCancellationQueue,
                        cancellationToken);
                    break;
                case TaskExecutionStrategy.Skip:
                    return;
            }

            if (readyToRun)
            {
                if (_nodeSessionService.GetNodeStatus(nodeSessionId) != NodeStatus.Online)
                {
                    return;
                }

                var rsp = await _nodeSessionService.SendTaskExecutionEventAsync(
                    nodeSessionId,
                   taskExecutionInstance.ToTriggerEvent(new TaskDefinitionModel()
                   {
                       Id = taskActivationRecord.TaskDefinitionId,
                       Value = taskDefinition
                   }, taskActivationRecord.EnvironmentVariables),
                    cancellationToken);
                _logger.LogInformation($"{delayMessage.Id}:SendTaskExecutionEventAsync");
                await _taskExecutionReportBatchQueue.EnqueueAsync(new TaskExecutionReport
                {
                    Id = delayMessage.Id,
                    Status = TaskExecutionStatus.Triggered,
                    Message = rsp.Message
                });
                _logger.LogInformation($"{delayMessage.Id}:SendAsync Triggered");
                delayMessage.Handled = true;
                return;
            }
        }
        catch (TaskCanceledException ex)
        {
            _exceptionCounter.AddOrUpdate(ex, delayMessage.Id);
            _logger.LogError($"{delayMessage.Id}:{ex}");
        }
        catch (OperationCanceledException ex)
        {
            if (ex.CancellationToken == cancellationToken)
            {
            }

            _exceptionCounter.AddOrUpdate(ex, delayMessage.Id);
            _logger.LogError($"{delayMessage.Id}:{ex}");
        }
        catch (Exception ex)
        {
            var exString = ex.ToString();
            _exceptionCounter.AddOrUpdate(ex, delayMessage.Id);
            _logger.LogError(exString);
            await SendTaskExecutionReportAsync(
                delayMessage.Id,
                TaskExecutionStatus.Failed,
                exString);
        }
        finally
        {
            _taskPenddingContextManager.RemoveContext(delayMessage.Id, out _);
        }
        if (cancellationToken.IsCancellationRequested)
        {
            delayMessage.Handled = true;
            await SendTaskExecutionReportAsync(
                delayMessage.Id,
                TaskExecutionStatus.PenddingTimeout,
                $"Time out after waiting {penddingLimitTimeSeconds} seconds");
            _logger.LogInformation($"{delayMessage.Id}:SendAsync PenddingTimeout");
            delayMessage.Handled = true;
        }
    }

    public async Task SendTaskExecutionReportAsync(
        string taskExecutionInstanceId,
        TaskExecutionStatus status,
        string message)
    {
        await _taskExecutionReportBatchQueue.EnqueueAsync(new TaskExecutionReport
        {
            Id = taskExecutionInstanceId,
            Status = status,
            Message = message
        });
    }


    async ValueTask ProcessTaskActivationRecordCreateResultAsync(
        TaskActivationRecordResult result,
        CancellationToken cancellationToken = default)
    {
        var taskDefinition = result.TaskActivationRecord.GetTaskDefinition();
        if (taskDefinition == null)
        {
            return;
        }
        foreach (var kv in result.Instances)
        {
            var taskExecutionInstance = kv.Value;
            TaskActivateServiceHelpers.ApplyEnvironmentVariables(taskDefinition);
            await SendTaskExecutionReportAsync(taskExecutionInstance.Id, TaskExecutionStatus.Triggered, string.Empty);

            await _delayMessageQueue.EnqueueAsync(new KafkaDelayMessage()
            {
                Id = taskExecutionInstance.Id,
                Type = nameof(TaskActivateService),
                SubType = SubType_PendingContextCheck,
                CreateDateTime = DateTime.UtcNow,
                Duration = TimeSpan.FromSeconds(5),
                ScheduleDateTime = DateTime.UtcNow + TimeSpan.FromSeconds(taskDefinition.PenddingLimitTimeSeconds),
            }, cancellationToken);

        }
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await ProcessTaskActivateServiceParametersAsync(cancellationToken);
    }

    private async Task ProcessTaskActivateServiceParametersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await foreach (var array in _serviceParametersBatchQueue.ReceiveAllAsync(cancellationToken))
            {
                try
                {
                    if (array == null)
                    {
                        continue;
                    }
                    foreach (var serviceParametersGroup in array.GroupBy(static x=>x.Parameters.Index))
                    {
                        switch (serviceParametersGroup.Key)
                        {
                            case 0:
                                await ProcessFireTaskParametersAsync(
                                    serviceParametersGroup.Select(x => x.Parameters.AsT0),
                                    cancellationToken);

                                break;
                            case 1:
                                await ProcessTaskFlowFireParametersAsync(
                                    serviceParametersGroup.Select(x => x.Parameters.AsT1),
                                    cancellationToken);
                                break;
                            case 2:
                                await ProcessRetryTaskParametersAsync(
                                    serviceParametersGroup.Select(x => x.Parameters.AsT2),
                                    cancellationToken);
                                break;
                            case 3:
                                await ProcessSwitchTaskFlowStageParametersAsync(
                                    serviceParametersGroup.Select(x => x.Parameters.AsT3),
                                    cancellationToken);
                                break;
                            default:
                                break;
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
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }

    }

    async ValueTask ProcessSwitchTaskFlowStageParametersAsync(
        IEnumerable<SwitchStageParameters> switchTaskFlowStageParameterList,
        CancellationToken cancellationToken)
    {
        foreach (var switchStageParametersGroup in switchTaskFlowStageParameterList.GroupBy(static x => x.TaskFlowExecutionInstanceId))
        {
            var taskFlowExecutionInstanceId = switchStageParametersGroup.Key;
            var lastSwitchTaskFlowStageParameters = switchStageParametersGroup.LastOrDefault();
            if (lastSwitchTaskFlowStageParameters == default)
            {
                continue;
            }
            await _taskFlowExecutor.SwitchStageAsync(
                taskFlowExecutionInstanceId,
                lastSwitchTaskFlowStageParameters.StageIndex,
                cancellationToken);
        }
    }

    async ValueTask ProcessRetryTaskParametersAsync(
        IEnumerable<RetryTaskParameters> retryTaskParameterList,
        CancellationToken cancellationToken = default)
    {
        await using var taskExecutionInstanceRepo = await _taskExecutionInstanceRepositoryFactory.CreateRepositoryAsync(cancellationToken);
        await using var taskActivationRecordRepo = await _taskActivationRecordRepoFactory.CreateRepositoryAsync(cancellationToken);
        foreach (var retryTaskFlowParametersGroup in retryTaskParameterList.GroupBy(static x => x.TaskExecutionInstanceId))
        {
            var taskExecutionInstanceId = retryTaskFlowParametersGroup.Key;
            if (taskExecutionInstanceId == null)
            {
                continue;
            }

            var taskExecutionInstance = await taskExecutionInstanceRepo.GetByIdAsync(taskExecutionInstanceId, cancellationToken);
            if (taskExecutionInstance == null)
            {
                continue;
            }
            if (!taskExecutionInstance.CanRetry())
            {
                continue;
            }

            var taskExecutionRecord = await taskActivationRecordRepo.GetByIdAsync(taskExecutionInstance.FireInstanceId, cancellationToken);
            if (taskExecutionRecord == null)
            {
                continue;
            }
            var taskDefinition = taskExecutionRecord.GetTaskDefinition();
            if (taskDefinition == null)
            {
                continue;
            }
            if (taskExecutionRecord.Value.NodeList == null)
            {
                continue;
            }
            var nodes = taskExecutionRecord.Value.NodeList.Where(x => x.Value == taskExecutionInstance.NodeInfoId).ToList();
            var fireTaskParameters = FireTaskParameters.BuildRetryTaskParameters(
                taskExecutionRecord.Id,
                taskExecutionRecord.TaskDefinitionId,
                taskExecutionInstance.FireInstanceId,
                nodes,
                taskExecutionRecord.Value.EnvironmentVariables,
                taskExecutionInstance.GetTaskFlowTaskKey());
            await _serviceParametersBatchQueue.SendAsync(new TaskActivateServiceParameters(fireTaskParameters), cancellationToken);


        }
    }

    async ValueTask ProcessTaskFlowFireParametersAsync(
        IEnumerable<FireTaskFlowParameters> fireTaskFlowParameterList,
        CancellationToken cancellationToken = default)
    {
        foreach (var fireTaskFlowParameters in fireTaskFlowParameterList)
        {
            await _taskFlowExecutor.CreateAsync(fireTaskFlowParameters, cancellationToken);
        }
    }



    async Task ProcessFireTaskParametersAsync(
        IEnumerable<FireTaskParameters> fireTaskParameterList,
        CancellationToken cancellationToken)
    {

        foreach (var fireTaskParameters in fireTaskParameterList)
        {
            var result = await _taskActivationRecordExecutor.CreateAsync(
                fireTaskParameters,
                cancellationToken);
            if (result.Index == 1)
            {
                continue;
            }
            await ProcessTaskActivationRecordCreateResultAsync(result.AsT0, cancellationToken);
        }
    }

}