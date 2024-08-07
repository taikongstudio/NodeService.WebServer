using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;
using OneOf;
using System.Collections.Immutable;

namespace NodeService.WebServer.Services.Tasks
{
    public class TaskActivationRecordExecutor
    {
        readonly ILogger<TaskActivationRecordExecutor> _logger;
        readonly ExceptionCounter _exceptionCounter;
        readonly ActionBlock<AsyncOperation<Func<ValueTask>>> _executionQueue;
        readonly ConfigurationQueryService _configurationQueryService;
        readonly INodeSessionService _nodeSessionService;
        readonly ApplicationRepositoryFactory<TaskActivationRecordModel> _taskActivationRecordRepoFactory;
        readonly ApplicationRepositoryFactory<TaskExecutionInstanceModel> _taskExecutionInstanceRepoFactory;
        readonly NodeInfoQueryService _nodeInfoQueryService;

        public TaskActivationRecordExecutor(
            INodeSessionService nodeSessionService,
            ILogger<TaskActivationRecordExecutor> logger,
            ExceptionCounter exceptionCounter,
            NodeInfoQueryService nodeInfoQueryService,
            ApplicationRepositoryFactory<TaskActivationRecordModel> taskActivationRecordRepoFactory,
            ApplicationRepositoryFactory<TaskExecutionInstanceModel> taskExecutionInstanceRepoFactory,
            ConfigurationQueryService configurationQueryService)
        {
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _executionQueue = new ActionBlock<AsyncOperation<Func<ValueTask>>>(ExecutionTaskAsync, new ExecutionDataflowBlockOptions()
            {
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
            _configurationQueryService = configurationQueryService;
            _nodeSessionService = nodeSessionService;
            _taskActivationRecordRepoFactory = taskActivationRecordRepoFactory;
            _taskExecutionInstanceRepoFactory = taskExecutionInstanceRepoFactory;
            _nodeInfoQueryService = nodeInfoQueryService;
        }

        async Task ExecutionTaskAsync(AsyncOperation<Func<ValueTask>> op)
        {
            await op.Argument.Invoke();
            op.TrySetResult();
        }

        public async ValueTask<OneOf<TaskActivationRecordResult, Exception>> CreateAsync(
            FireTaskParameters fireTaskParameters,
            CancellationToken cancellationToken = default)
        {
            OneOf<TaskActivationRecordResult, Exception> result = default;

            await ExecutionAsync(async (cancellationToken) =>
            {
                result = await CreateTaskActiveRecordCoreAsync(fireTaskParameters, cancellationToken);
            }, cancellationToken);

            return result;
        }

        async ValueTask<OneOf<TaskActivationRecordResult, Exception>> CreateTaskActiveRecordCoreAsync(
            FireTaskParameters fireTaskParameters,
            CancellationToken cancellationToken = default)
        {
            var taskDefinition = await GetTaskDefinitionAsync(
                fireTaskParameters.TaskDefinitionId,
                cancellationToken);
            if (taskDefinition == null)
            {
                return default;
            }

            if (string.IsNullOrEmpty(taskDefinition.TaskTypeDescId))
            {
                return default;
            }

            taskDefinition.TaskTypeDesc = await GetTaskTypeDescAsync(taskDefinition.TaskTypeDescId, cancellationToken);
            if (taskDefinition.TaskTypeDesc == null)
            {
                return default;
            }

            return await ActivateRemoteNodeTasksAsync(
                  fireTaskParameters,
                  taskDefinition,
                  fireTaskParameters.TaskFlowTaskKey,
                  cancellationToken);
        }

        async ValueTask AddTaskExecutionInstanceListAsync(
            IEnumerable<TaskExecutionInstanceModel> taskExecutionInstanceList,
            CancellationToken cancellationToken = default)
        {
            await using var taskExecutionInstanceRepo = await _taskExecutionInstanceRepoFactory.CreateRepositoryAsync(cancellationToken);
            await taskExecutionInstanceRepo.AddRangeAsync(taskExecutionInstanceList, cancellationToken);
        }

        public async ValueTask ExecutionAsync(
            Func<CancellationToken, ValueTask> func,
            CancellationToken cancellationToken = default)
        {
            var op = new AsyncOperation<Func<ValueTask>>(async () =>
            {
                await CallFunctionAsync(func, cancellationToken);

            }, AsyncOperationKind.AddOrUpdate);
            await _executionQueue.SendAsync(op, cancellationToken);
            await op.WaitAsync(cancellationToken);
        }

        private async ValueTask CallFunctionAsync(
            Func<CancellationToken, ValueTask> func,
            CancellationToken cancellationToken)
        {
            try
            {
                await func.Invoke(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                _exceptionCounter.AddOrUpdate(ex);
            }
        }

        static void AddTaskExecutionNodeInstances(
            List<KeyValuePair<NodeSessionId, TaskExecutionInstanceModel>> taskExecutionInstanceList,
            StringEntry? nodeEntry,
            TaskExecutionNodeInfo taskExecutionNodeInfo)
        {
            foreach (var item in taskExecutionInstanceList)
            {
                if (item.Key.NodeId.Value == nodeEntry.Value)
                {
                    taskExecutionNodeInfo.Instances.Add(new TaskExecutionInstanceInfo()
                    {
                        TaskExecutionInstanceId = item.Value.Id,
                        Status = TaskExecutionStatus.Unknown,
                        Message = item.Value.Message,
                    });
                    break;
                }
            }
        }

        void BuildTaskExecutionInstanceList(
            List<KeyValuePair<NodeSessionId, TaskExecutionInstanceModel>> taskExecutionInstanceList,
            FireTaskParameters fireTaskParameters,
            TaskDefinitionModel taskDefinition,
            TaskFlowTaskKey taskFlowTaskKey,
            IEnumerable<StringEntry> nodeList,
            IEnumerable<NodeInfoModel> nodeInfoList,
            CancellationToken cancellationToken)
        {
            foreach (var nodeEntry in nodeList)
            {
                try
                {
                    if (string.IsNullOrEmpty(nodeEntry.Value)) continue;
                    var nodeFindResult = FindNodeInfo(nodeInfoList, nodeEntry.Value);

                    if (nodeFindResult.NodeId.IsNullOrEmpty) continue;

                    var nodeSessionIdList = _nodeSessionService.EnumNodeSessions(nodeFindResult.NodeId).ToArray();

                    if (nodeSessionIdList.Length == 0)
                    {
                        var nodeSessionId = new NodeSessionId(nodeFindResult.NodeId.Value);
                        nodeSessionIdList = [nodeSessionId];
                    }
                    foreach (var nodeSessionId in nodeSessionIdList)
                    {
                        var taskExecutionInstance = BuildTaskExecutionInstance(
                            nodeFindResult.NodeInfo,
                            taskDefinition,
                            nodeSessionId,
                            fireTaskParameters,
                            taskFlowTaskKey);
                        taskExecutionInstanceList.Add(KeyValuePair.Create(nodeSessionId, taskExecutionInstance));
                    }
                }
                catch (Exception ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }
            }
        }

        ValueTask<List<NodeInfoModel>> QueryNodeListAsync(
            TaskDefinitionModel taskDefinition,
            CancellationToken cancellationToken = default)
        {
            return _nodeInfoQueryService.QueryNodeInfoListAsync(taskDefinition.NodeList.Select(static x => x.Value), true, cancellationToken);
        }

        static (NodeId NodeId, NodeInfoModel NodeInfo) FindNodeInfo(
            IEnumerable<NodeInfoModel> nodeInfoList,
            string id)
        {
            foreach (var nodeInfo in nodeInfoList)
            {
                if (nodeInfo.Id == id)
                {
                    return (new NodeId(nodeInfo.Id), nodeInfo);
                }
            }

            return default;
        }

        TaskExecutionInstanceModel BuildTaskExecutionInstance(
    NodeInfoModel nodeInfo,
    TaskDefinitionModel taskDefinition,
    NodeSessionId nodeSessionId,
    FireTaskParameters parameters,
    TaskFlowTaskKey taskFlowTaskKey = default)
        {
            var nodeName = _nodeSessionService.GetNodeName(nodeSessionId) ?? nodeInfo.Name;
            var taskExecutionInstance = new TaskExecutionInstanceModel
            {
                Id = Guid.NewGuid().ToString(),
                Name = $"{nodeName} {taskDefinition.Name}",
                NodeInfoId = nodeSessionId.NodeId.Value,
                Status = TaskExecutionStatus.Unknown,
                FireTimeUtc = parameters.FireTimeUtc.DateTime,
                Message = string.Empty,
                FireType = "Server",
                TriggerSource = parameters.TriggerSource,
                TaskDefinitionId = taskDefinition.Id,
                ParentId = parameters.ParentTaskExecutionInstanceId,
                FireInstanceId = parameters.TaskActivationRecordId
            };
            switch (taskDefinition.ExecutionStrategy)
            {
                case TaskExecutionStrategy.Concurrent:
                    taskExecutionInstance.Message = $"{nodeName}: triggered";
                    break;
                case TaskExecutionStrategy.Queue:
                    taskExecutionInstance.Message = $"{nodeName}: waiting for any task";
                    break;
                case TaskExecutionStrategy.Skip:
                    taskExecutionInstance.Message = $"{nodeName}: start job";
                    break;
                case TaskExecutionStrategy.Stop:
                    taskExecutionInstance.Message = $"{nodeName}: waiting for kill all job";
                    break;
            }

            if (parameters.NextFireTimeUtc != null)
                taskExecutionInstance.NextFireTimeUtc = parameters.NextFireTimeUtc.Value.UtcDateTime;
            if (parameters.PreviousFireTimeUtc != null)
                taskExecutionInstance.NextFireTimeUtc = parameters.PreviousFireTimeUtc.Value.UtcDateTime;
            if (parameters.ScheduledFireTimeUtc != null)
                taskExecutionInstance.ScheduledFireTimeUtc = parameters.ScheduledFireTimeUtc.Value.UtcDateTime;

            taskExecutionInstance.TaskFlowTemplateId = taskFlowTaskKey.TaskFlowTemplateId;
            taskExecutionInstance.TaskFlowInstanceId = taskFlowTaskKey.TaskFlowInstanceId;
            taskExecutionInstance.TaskFlowStageId = taskFlowTaskKey.TaskFlowStageId;
            taskExecutionInstance.TaskFlowGroupId = taskFlowTaskKey.TaskFlowGroupId;
            taskExecutionInstance.TaskFlowTaskId = taskFlowTaskKey.TaskFlowTaskId;
            return taskExecutionInstance;
        }


        async ValueTask<OneOf<TaskActivationRecordResult, Exception>> ActivateRemoteNodeTasksAsync(
            FireTaskParameters fireTaskParameters,
            TaskDefinitionModel taskDefinition,
            TaskFlowTaskKey taskFlowTaskKey = default,
            CancellationToken cancellationToken = default)
        {
            TaskActivationRecordModel? taskActivationRecord = null;
            try
            {
                var taskExecutionInstanceList = new List<KeyValuePair<NodeSessionId, TaskExecutionInstanceModel>>();

                if (fireTaskParameters.RetryTasks)
                {
                    await using var taskActivationRecordRepo = await _taskActivationRecordRepoFactory.CreateRepositoryAsync(cancellationToken);
                    taskActivationRecord = await taskActivationRecordRepo.GetByIdAsync(fireTaskParameters.TaskActivationRecordId, cancellationToken);
                    if (taskActivationRecord == null)
                    {
                        return new Exception("invalid fire instance id");
                    }
                    if (taskActivationRecord.Status == TaskExecutionStatus.Finished)
                    {
                        return new Exception("invalid status");
                    }
                    var nodeInfoList = await QueryNodeListAsync(taskDefinition, cancellationToken);

                    foreach (var taskExecutionNodeInfo in taskActivationRecord.Value.TaskExecutionNodeList)
                    {
                        if (taskExecutionNodeInfo.Status == TaskExecutionStatus.Finished)
                        {
                            continue;
                        }
                        var lastInstance = taskExecutionNodeInfo.Instances.LastOrDefault();
                        if (lastInstance == null)
                        {
                            continue;
                        }

                        var canRetry = lastInstance.Status switch
                        {
                            TaskExecutionStatus.Failed or TaskExecutionStatus.PenddingTimeout or TaskExecutionStatus.Failed => true,
                            _ => false
                        };

                        if (!canRetry)
                        {
                            continue;
                        }
                        var nodeInfoFindResult = FindNodeInfo(nodeInfoList, taskExecutionNodeInfo.NodeInfoId);
                        if (nodeInfoFindResult == default)
                        {
                            continue;
                        }

                        var nodeEntries = taskDefinition.NodeList
                            .Where(x => x.Value == taskExecutionNodeInfo.NodeInfoId).ToArray();
                        if (nodeEntries.Length == 0)
                        {
                            continue;
                        }

                        BuildTaskExecutionInstanceList(
                            taskExecutionInstanceList,
                            fireTaskParameters,
                            taskDefinition,
                            taskFlowTaskKey,
                            nodeEntries,
                            [nodeInfoFindResult.NodeInfo],
                            cancellationToken);

                        foreach (var nodeEntry in nodeEntries)
                        {
                            if (nodeEntry is null)
                            {
                                continue;
                            }

                            AddTaskExecutionNodeInstances(taskExecutionInstanceList, nodeEntry, taskExecutionNodeInfo);
                        }

                    }

                    await AddTaskExecutionInstanceListAsync(
                            taskExecutionInstanceList.Select(static x => x.Value),
                            cancellationToken);

                    await taskActivationRecordRepo.UpdateAsync(taskActivationRecord, cancellationToken);

                }
                else
                {
                    if (fireTaskParameters.NodeList.Length > 0)
                    {
                        taskDefinition.NodeList = [.. fireTaskParameters.NodeList];
                    }
                    if (fireTaskParameters.EnvironmentVariables.Length > 0)
                    {
                        foreach (var item in fireTaskParameters.EnvironmentVariables)
                        {
                            var entry = taskDefinition.Value.EnvironmentVariables.Find(x => x.Name == item.Name);
                            if (entry == null)
                            {
                                taskDefinition.Value.EnvironmentVariables.Add(item);
                            }
                            else
                            {
                                entry.Value = item.Value;
                            }
                        }
                    }
                    var nodeInfoList = await QueryNodeListAsync(taskDefinition, cancellationToken);

                    BuildTaskExecutionInstanceList(
                        taskExecutionInstanceList,
                        fireTaskParameters,
                        taskDefinition,
                        taskFlowTaskKey,
                        taskDefinition.NodeList,
                        nodeInfoList,
                        cancellationToken);

                    await AddTaskExecutionInstanceListAsync(
                        taskExecutionInstanceList.Select(static x => x.Value),
                        cancellationToken);

                    List<TaskExecutionNodeInfo> taskExecutionNodeList = [];

                    foreach (var nodeEntry in taskDefinition.NodeList)
                    {
                        if (nodeEntry?.Value == null)
                        {
                            continue;
                        }
                        var taskExecutionNodeInfo = new TaskExecutionNodeInfo(fireTaskParameters.TaskActivationRecordId, nodeEntry.Value);
                        AddTaskExecutionNodeInstances(taskExecutionInstanceList, nodeEntry, taskExecutionNodeInfo);
                        taskExecutionNodeList.Add(taskExecutionNodeInfo);
                    }

                    taskActivationRecord = new TaskActivationRecordModel
                    {
                        CreationDateTime = DateTime.UtcNow,
                        ModifiedDateTime = DateTime.UtcNow,
                        Id = fireTaskParameters.TaskActivationRecordId,
                        TaskDefinitionId = taskDefinition.Id,
                        Name = taskDefinition.Name,
                        TaskDefinitionJson = JsonSerializer.Serialize(taskDefinition.Value),
                        TaskExecutionNodeList = taskExecutionNodeList,
                        TotalCount = taskExecutionNodeList.Count,
                        Status = TaskExecutionStatus.Unknown,
                        TaskFlowTemplateId = taskFlowTaskKey.TaskFlowTemplateId,
                        TaskFlowTaskId = taskFlowTaskKey.TaskFlowTaskId,
                        TaskFlowInstanceId = taskFlowTaskKey.TaskFlowInstanceId,
                        TaskFlowGroupId = taskFlowTaskKey.TaskFlowGroupId,
                        TaskFlowStageId = taskFlowTaskKey.TaskFlowStageId,
                        NodeList = [.. taskDefinition.NodeList],
                        EnvironmentVariables = [.. fireTaskParameters.EnvironmentVariables]
                    };
                    await using var taskActivationRecordRepo = await _taskActivationRecordRepoFactory.CreateRepositoryAsync(cancellationToken);
                    await taskActivationRecordRepo.AddAsync(taskActivationRecord, cancellationToken);
                }
                return new TaskActivationRecordResult(fireTaskParameters, taskActivationRecord, taskExecutionInstanceList.ToImmutableArray());
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
                return ex;
            }
        }



        async ValueTask<TaskTypeDescConfigModel?> GetTaskTypeDescAsync(
            string taskTypeDescId,
            CancellationToken cancellationToken = default)
        {
            var list = await _configurationQueryService.QueryConfigurationByIdListAsync<TaskTypeDescConfigModel>([taskTypeDescId], cancellationToken);
            return list.Items?.FirstOrDefault();
        }

        async ValueTask<TaskDefinitionModel?> GetTaskDefinitionAsync(
            string taskDefinitionId,
            CancellationToken cancellationToken = default)
        {
            var list = await _configurationQueryService.QueryConfigurationByIdListAsync<TaskDefinitionModel>([taskDefinitionId], cancellationToken);
            return list.Items?.FirstOrDefault();
        }

    }
}
