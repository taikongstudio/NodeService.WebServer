using NodeService.Infrastructure.DataModels;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.TaskSchedule;
using OneOf;
using System.Collections.Immutable;
using System.Reactive.Disposables;

namespace NodeService.WebServer.Services.Tasks
{

    public class TaskFlowExecutor
    {
        readonly ILogger<TaskFlowExecutor> _logger;
        readonly ExceptionCounter _exceptionCounter;
        readonly BatchQueue<TaskActivateServiceParameters> _taskActivateServiceParametersBatchQueue;
        readonly ApplicationRepositoryFactory<TaskFlowTemplateModel> _taskFlowTemplateRepoFactory;
        readonly ApplicationRepositoryFactory<TaskFlowExecutionInstanceModel> _taskFlowExecutionInstanceRepoFactory;
        readonly ApplicationRepositoryFactory<TaskActivationRecordModel> _taskActivationRecordRepoFactory;
        readonly ConcurrentDictionary<int, ActionBlock<AsyncOperation<Func<Task>>>> _executionQueueDict;
        readonly IAsyncQueue<KafkaDelayMessage> _delayMessageQueue;
        readonly IDelayMessageBroadcast _delayMessageBroadcast;
        private ConfigurationQueryService _configurationQueryService;
        readonly IAsyncQueue<TaskObservationEvent> _eventQueue;
        const string SubType_ExecutionTimeLimit = "ExecutionTimeLimit";
        const string SubTyoe_TaskFlowStageExecutionTimeLimit = "TaskFlowStageExecutionTimeLimit";

        public TaskFlowExecutor(
            ILogger<TaskFlowExecutor> logger,
            ExceptionCounter exceptionCounter,
            BatchQueue<TaskActivateServiceParameters> taskActivateServiceBatchQueue,
            ApplicationRepositoryFactory<TaskFlowTemplateModel> taskFlowTemplateRepoFactory,
            ApplicationRepositoryFactory<TaskFlowExecutionInstanceModel> taskFlowExecutionInstanceRepoFactory,
            ApplicationRepositoryFactory<TaskActivationRecordModel> taskActivationRecordRepoFactory,
            IAsyncQueue<KafkaDelayMessage> delayMessageQueue,
            IDelayMessageBroadcast delayMessageBroadcast,
            ConfigurationQueryService configurationQueryService,
            IAsyncQueue<TaskObservationEvent> eventQueue
            )
        {
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _taskActivateServiceParametersBatchQueue = taskActivateServiceBatchQueue;
            _taskFlowTemplateRepoFactory = taskFlowTemplateRepoFactory;
            _taskFlowExecutionInstanceRepoFactory = taskFlowExecutionInstanceRepoFactory;
            _taskActivationRecordRepoFactory = taskActivationRecordRepoFactory;
            _executionQueueDict = new ConcurrentDictionary<int, ActionBlock<AsyncOperation<Func<Task>>>>();
            for (var startIndex = '0'; startIndex < 'z'; startIndex++)
            {
                var key = Math.DivRem(startIndex, 10, out int result);
                _executionQueueDict.GetOrAdd(key, new ActionBlock<AsyncOperation<Func<Task>>>(ExecutionTaskAsync, new ExecutionDataflowBlockOptions()
                {
                    EnsureOrdered = true,
                    MaxDegreeOfParallelism = 1
                }));
            }

            _delayMessageQueue = delayMessageQueue;
            _delayMessageBroadcast = delayMessageBroadcast;
            _delayMessageBroadcast.AddHandler(nameof(TaskFlowExecutor), ProcessDelayMessage);
            _configurationQueryService = configurationQueryService;
            _eventQueue = eventQueue;
        }

        ActionBlock<AsyncOperation<Func<Task>>> GetActionBlock(string id)
        {
            var key = Math.DivRem(id[0], 10, out int result);
            return _executionQueueDict[key];
        }

        async ValueTask ProcessDelayMessage(KafkaDelayMessage kafkaDelayMessage, CancellationToken cancellationToken = default)
        {
            switch (kafkaDelayMessage.SubType)
            {
                case SubType_ExecutionTimeLimit:
                    {
                        await ProcessTaskFlowExecutionTimeLimitReachedAsync(kafkaDelayMessage.Id, cancellationToken);
                        kafkaDelayMessage.Handled = true;
                    }
                    break;
                case SubTyoe_TaskFlowStageExecutionTimeLimit:
                    {
                        var stageId = kafkaDelayMessage.Properties["StageId"];
                        await ProcessTaskFlowStageExecutionTimeLimitReachedAsync(
                            new TaskFlowExecutionTimeLimitParameters(kafkaDelayMessage.Id, stageId),
                            cancellationToken);
                        kafkaDelayMessage.Handled = true;
                    }
                    break;
                default:
                    break;
            }
        }

        public async ValueTask ProcessTaskFlowExecutionTimeLimitReachedAsync(string taskFlowExecutionInstanceId, CancellationToken cancellationToken = default)
        {
            var op = new AsyncOperation<Func<Task>>(async () =>
            {
                try
                {
                    await using var taskFlowRepo = await _taskFlowExecutionInstanceRepoFactory.CreateRepositoryAsync(cancellationToken);
                    var taskFlowExecutionInstance = await taskFlowRepo.GetByIdAsync(taskFlowExecutionInstanceId, cancellationToken);
                    if (taskFlowExecutionInstance == null)
                    {
                        return;
                    }
                    if (taskFlowExecutionInstance.Status == TaskFlowExecutionStatus.Finished || taskFlowExecutionInstance.Status == TaskFlowExecutionStatus.Fault)
                    {
                        return;
                    }
                    taskFlowExecutionInstance.Status = TaskFlowExecutionStatus.Fault;
                    taskFlowExecutionInstance.Value.Message = $"Execution time limit!";
                    await taskFlowRepo.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.ToString());
                    _exceptionCounter.AddOrUpdate(ex);
                }

            }, AsyncOperationKind.AddOrUpdate);
            await GetActionBlock(taskFlowExecutionInstanceId).SendAsync(op, cancellationToken);
            await op.WaitAsync(cancellationToken);
        }

        async Task ExecutionTaskAsync(AsyncOperation<Func<Task>> op)
        {
            await op.Argument.Invoke();
            op.TrySetResult();
        }

        async ValueTask<OneOf<TaskFlowExecutionInstanceModel?, Exception>> CreateTaskFlowExecutionInstance(FireTaskFlowParameters fireTaskFlowParameters, CancellationToken cancellationToken = default)
        {
            try
            {
                OneOf<TaskFlowExecutionInstanceModel?, Exception> oneOf = default;
                var taskFlowTemplate = await GetTaskFlowTemplateAsync(fireTaskFlowParameters.TaskFlowTemplateId, cancellationToken);
                if (taskFlowTemplate == null)
                {
                    oneOf = new Exception("invalid template id");
                    return oneOf;
                }
                if (fireTaskFlowParameters.EnvironmentVariables != null && fireTaskFlowParameters.EnvironmentVariables.Count > 0)
                {
                    foreach (var item in fireTaskFlowParameters.EnvironmentVariables)
                    {
                        var entry = taskFlowTemplate.EnvironmentVariables.Find(x => x.Name == item.Name);
                        if (entry == null)
                        {
                            taskFlowTemplate.EnvironmentVariables.Add(item);
                        }
                        else
                        {
                            entry.Value = item.Value;
                        }
                    }
                }
                var taskFlowExecutionInstance = new TaskFlowExecutionInstanceModel()
                {
                    Id = fireTaskFlowParameters.TaskFlowInstanceId,
                    Name = taskFlowTemplate.Name,
                    CreationDateTime = DateTime.UtcNow,
                    ModifiedDateTime = DateTime.UtcNow,
                    TaskFlowTemplateId = taskFlowTemplate.Id,
                };
                taskFlowExecutionInstance.Value.Id = fireTaskFlowParameters.TaskFlowInstanceId;
                taskFlowExecutionInstance.Value.Name = taskFlowTemplate.Name;
                taskFlowExecutionInstance.Value.CreationDateTime = DateTime.UtcNow;
                taskFlowExecutionInstance.Value.ModifiedDateTime = DateTime.UtcNow;
                taskFlowExecutionInstance.Value.TaskFlowTemplateId = taskFlowTemplate.Id;
                taskFlowExecutionInstance.Value.TaskFlowTemplateJson = JsonSerializer.Serialize(taskFlowTemplate);
                foreach (var taskFlowStageTemplate in taskFlowTemplate.Value.TaskStages)
                {
                    var taskFlowStageExecutionInstance = new TaskFlowStageExecutionInstance()
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = taskFlowStageTemplate.Name,
                        CreationDateTime = DateTime.UtcNow,
                        ModifiedDateTime = DateTime.UtcNow,
                        TaskFlowStageTemplateId = taskFlowStageTemplate.Id
                    };
                    taskFlowExecutionInstance.Value.TaskStages.Add(taskFlowStageExecutionInstance);
                    foreach (var taskFlowGroupTemplate in taskFlowStageTemplate.TaskGroups)
                    {
                        var taskFlowGroupExeuctionInstance = new TaskFlowGroupExecutionInstance()
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = taskFlowGroupTemplate.Name,
                            CreationDateTime = DateTime.UtcNow,
                            ModifiedDateTime = DateTime.UtcNow,
                            TaskFlowGroupTemplateId = taskFlowGroupTemplate.Id
                        };
                        taskFlowStageExecutionInstance.TaskGroups.Add(taskFlowGroupExeuctionInstance);
                        foreach (var taskFlowTaskTemplate in taskFlowGroupTemplate.Tasks)
                        {
                            var taskFlowTaskExecutionInstance = new TaskFlowTaskExecutionInstance()
                            {
                                Id = Guid.NewGuid().ToString(),
                                Name = taskFlowTaskTemplate.Name,
                                CreationDateTime = DateTime.UtcNow,
                                ModifiedDateTime = DateTime.UtcNow,
                                Status = TaskExecutionStatus.Unknown,
                                TaskFlowTaskTemplateId = taskFlowTaskTemplate.Id,
                                TaskDefinitionId = taskFlowTaskTemplate.TaskDefinitionId,
                            };
                            taskFlowGroupExeuctionInstance.Tasks.Add(taskFlowTaskExecutionInstance);
                        }
                    }
                }

                await ExecuteTaskFlowAsync(taskFlowExecutionInstance, cancellationToken);

                await using var taskFlowExeuctionInstanceRepo = await _taskFlowExecutionInstanceRepoFactory.CreateRepositoryAsync();
                await taskFlowExeuctionInstanceRepo.AddAsync(taskFlowExecutionInstance, cancellationToken);
                return taskFlowExecutionInstance;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                _exceptionCounter.AddOrUpdate(ex);
                return ex;
            }

        }

        async ValueTask<TaskFlowTemplateModel?> GetTaskFlowTemplateAsync(
string taskFlowTemplateId,
CancellationToken cancellationToken = default)
        {
            var list = await _configurationQueryService.QueryConfigurationByIdListAsync<TaskFlowTemplateModel>([taskFlowTemplateId], cancellationToken);
            return list.Items?.FirstOrDefault();
        }

        public async ValueTask CreateAsync(
            FireTaskFlowParameters fireTaskFlowParameters,
            CancellationToken cancellationToken = default)
        {
            var op = new AsyncOperation<Func<Task>>(async () =>
            {
                var taskFlowExecutionInstance = await CreateTaskFlowExecutionInstance(fireTaskFlowParameters, cancellationToken);
            }, AsyncOperationKind.None);
            await GetActionBlock(fireTaskFlowParameters.TaskFlowTemplateId).SendAsync(op, cancellationToken);
            await op.WaitAsync(cancellationToken);
        }

        public async ValueTask ExecuteAsync(
            string taskFlowExecutionInstanceId,
            ImmutableArray<TaskActivationRecordModel> taskActivationRecords,
            CancellationToken cancellationToken = default)
        {
            var op = new AsyncOperation<Func<Task>>(async () =>
            {
                await ExecuteTaskFlowAsync(taskFlowExecutionInstanceId, taskActivationRecords, cancellationToken);
            }, AsyncOperationKind.None);
            await GetActionBlock(taskFlowExecutionInstanceId).SendAsync(op, cancellationToken);
            await op.WaitAsync(cancellationToken);
        }

        private async Task ExecuteTaskFlowAsync(string taskFlowInstanceId, ImmutableArray<TaskActivationRecordModel> taskActivationRecords, CancellationToken cancellationToken)
        {
            await using var taskFlowExecutionInstanceRepo = await _taskFlowExecutionInstanceRepoFactory.CreateRepositoryAsync(cancellationToken);
            var taskFlowExecutionInstance = await taskFlowExecutionInstanceRepo.GetByIdAsync(taskFlowInstanceId, cancellationToken);
            if (taskFlowExecutionInstance == null)
            {
                return;
            }
            foreach (var activationRecord in taskActivationRecords)
            {
                var taskStage = taskFlowExecutionInstance.TaskStages.FirstOrDefault(x => x.Id == activationRecord.TaskFlowStageId);
                if (taskStage == null)
                {
                    continue;
                }
                var taskGroup = taskStage.TaskGroups.FirstOrDefault(x => x.Id == activationRecord.TaskFlowGroupId);
                if (taskGroup == null)
                {
                    continue;
                }
                var taskFlowTaskExecutionInstance = taskGroup.Tasks.FirstOrDefault(x => x.Id == activationRecord.TaskFlowTaskId);
                if (taskFlowTaskExecutionInstance == null)
                {
                    continue;
                }
                switch (activationRecord.Status)
                {
                    case TaskExecutionStatus.Unknown:
                        taskFlowTaskExecutionInstance.Status = TaskExecutionStatus.Unknown;
                        break;
                    case TaskExecutionStatus.Triggered:
                    case TaskExecutionStatus.Pendding:
                    case TaskExecutionStatus.Started:
                    case TaskExecutionStatus.Running:
                        taskFlowTaskExecutionInstance.Status = TaskExecutionStatus.Running;
                        taskFlowTaskExecutionInstance.FinishedCount = activationRecord.Value.FinishedCount;
                        taskFlowTaskExecutionInstance.TotalCount = activationRecord.Value.TotalCount;
                        break;
                    case TaskExecutionStatus.Failed:
                    case TaskExecutionStatus.PenddingTimeout:
                    case TaskExecutionStatus.Cancelled:
                        taskFlowTaskExecutionInstance.Status = activationRecord.Status;
                        taskFlowTaskExecutionInstance.FinishedCount = activationRecord.Value.FinishedCount;
                        taskFlowTaskExecutionInstance.TotalCount = activationRecord.Value.TotalCount;
                        break;
                    case TaskExecutionStatus.Finished:
                        taskFlowTaskExecutionInstance.Status = TaskExecutionStatus.Finished;
                        taskFlowTaskExecutionInstance.FinishedCount = activationRecord.Value.FinishedCount;
                        taskFlowTaskExecutionInstance.TotalCount = activationRecord.Value.TotalCount;
                        break;
                    case TaskExecutionStatus.MaxCount:
                        break;
                    default:
                        break;
                }
                taskFlowTaskExecutionInstance.TaskActiveRecordId = activationRecord.Id;
            }
            taskFlowExecutionInstance.Value = taskFlowExecutionInstance.Value with { };

            await ExecuteTaskFlowAsync(taskFlowExecutionInstance, cancellationToken);

            await taskFlowExecutionInstanceRepo.UpdateAsync(taskFlowExecutionInstance, cancellationToken);
        }

        public async ValueTask SwitchStageAsync(
            string taskFlowExecutionInstanceId,
            int stageIndex,
            CancellationToken cancellationToken = default)
        {
            var op = new AsyncOperation<Func<Task>>(async () =>
            {
                await SwitchStageIndexAsync(
                    taskFlowExecutionInstanceId,
                    stageIndex,
                    cancellationToken);
            }, AsyncOperationKind.None);
            await GetActionBlock(taskFlowExecutionInstanceId).SendAsync(op, cancellationToken);
            await op.WaitAsync(cancellationToken);
        }

        async ValueTask SwitchStageIndexAsync(
            string taskFlowExecutionInstanceId,
            int stageIndex,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await using var taskFlowExecutionInstanceRepo = await _taskFlowExecutionInstanceRepoFactory.CreateRepositoryAsync(cancellationToken);
                var taskFlowExecutionInstance = await taskFlowExecutionInstanceRepo.GetByIdAsync(taskFlowExecutionInstanceId, cancellationToken);
                if (taskFlowExecutionInstance == null)
                {
                    return;
                }
                bool resetStage = false;
                foreach (var taskFlowStageExecutionInstance in taskFlowExecutionInstance.Value.TaskStages.Skip(stageIndex))
                {
                    if (!resetStage)
                    {
                        taskFlowStageExecutionInstance.RetryTasks = true;
                    }
                    if (resetStage)
                    {
                        taskFlowStageExecutionInstance.Status = TaskFlowExecutionStatus.Unknown;
                    }
                    if (!resetStage)
                    {
                        resetStage = true;
                    }
                }
                taskFlowExecutionInstance.Value.CurrentStageIndex = stageIndex;
                await this.ExecuteTaskFlowAsync(taskFlowExecutionInstance, cancellationToken);
                taskFlowExecutionInstance.ModifiedDateTime = DateTime.UtcNow;
                await taskFlowExecutionInstanceRepo.UpdateAsync(taskFlowExecutionInstance, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                _exceptionCounter.AddOrUpdate(ex);
            }

        }

        async ValueTask ExecuteTaskFlowAsync(
            TaskFlowExecutionInstanceModel taskFlowExecutionInstance,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (taskFlowExecutionInstance.Status == TaskFlowExecutionStatus.Fault)
                {
                    return;
                }
                var taskFlowTemplate = JsonSerializer.Deserialize<TaskFlowTemplateModel>(taskFlowExecutionInstance.Value.TaskFlowTemplateJson);
                if (taskFlowTemplate == null)
                {
                    return;
                }
                var currentStageIndex = taskFlowExecutionInstance.Value.CurrentStageIndex;
                do
                {
                    var taskFlowStageExecutionInstance = taskFlowExecutionInstance.TaskStages.ElementAtOrDefault(currentStageIndex);
                    if (taskFlowStageExecutionInstance == null)
                    {
                        currentStageIndex = taskFlowExecutionInstance.TaskStages.Count - 1;
                        break;
                    }
                    var taskStageTemplate = taskFlowTemplate.Value.TaskStages.FirstOrDefault(x => x.Id == taskFlowStageExecutionInstance.TaskFlowStageTemplateId);
                    if (taskFlowStageExecutionInstance.IsTerminatedStatus())
                    {
                        currentStageIndex++;
                        continue;
                    }
                    await ExecuteTaskStageAsync(
                                taskFlowTemplate,
                                taskFlowExecutionInstance,
                                taskFlowStageExecutionInstance,
                                cancellationToken);
                    taskFlowStageExecutionInstance.RetryTasks = false;
                    if (taskFlowStageExecutionInstance.Status == TaskFlowExecutionStatus.Finished)
                    {
                        currentStageIndex++;
                        continue;
                    }
                    break;
                }
                while (true);
                taskFlowExecutionInstance.Value.CurrentStageIndex = currentStageIndex;
                bool statusChanged = false;
                if (taskFlowExecutionInstance.TaskStages.All(static x => x.Status == TaskFlowExecutionStatus.Finished))
                {
                    taskFlowExecutionInstance.Status = TaskFlowExecutionStatus.Finished;
                    statusChanged = true;
                }
                else if (taskFlowExecutionInstance.Value.TaskStages.Any(static x => x.Status != TaskFlowExecutionStatus.Finished))
                {
                    taskFlowExecutionInstance.Status = TaskFlowExecutionStatus.Running;
                    statusChanged = true;
                }
                else if (taskFlowExecutionInstance.Value.TaskStages.Any(static x => x.Status == TaskFlowExecutionStatus.Fault))
                {
                    taskFlowExecutionInstance.Status = TaskFlowExecutionStatus.Fault;
                    statusChanged = true;
                }
                if (statusChanged)
                {
                    await SendTaskObservationEventAsync(taskFlowExecutionInstance, cancellationToken);
                }
                taskFlowExecutionInstance.ModifiedDateTime = DateTime.UtcNow;

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                _exceptionCounter.AddOrUpdate(ex);
            }

        }

        private async ValueTask SendTaskObservationEventAsync(TaskFlowExecutionInstanceModel taskFlowExecutionInstance, CancellationToken cancellationToken)
        {
            var taskObservationConfiguration = await _configurationQueryService.QueryTaskObservationConfigurationAsync(cancellationToken);
            if (taskObservationConfiguration == null)
            {
                return;
            }
            if (!taskObservationConfiguration.IsEnabled)
            {
                return;
            }
            foreach (var item in taskObservationConfiguration.TaskFlowObservations)
            {
                if (!item.IsEnabled)
                {
                    continue;
                }
                if (item.Status != taskFlowExecutionInstance.Status)
                {
                    continue;
                }
                await _eventQueue.EnqueueAsync(new TaskObservationEvent()
                {
                    Id = taskFlowExecutionInstance.Id,
                    Context = string.Empty,
                    CreationDateTime = taskFlowExecutionInstance.CreationDateTime,
                    Message = taskFlowExecutionInstance.Message,
                    Name = taskFlowExecutionInstance.Name,
                    Status = (int)taskFlowExecutionInstance.Status,
                    Type = "TaskFlowExecutionInstanceModel"
                }, cancellationToken);
            }

        }

        async ValueTask ExecuteTaskStageAsync(
            TaskFlowTemplateModel taskFlowTemplate,
            TaskFlowExecutionInstanceModel taskFlowExecutionInstance,
            TaskFlowStageExecutionInstance taskFlowStageExecutionInstance,
            CancellationToken cancellationToken = default)
        {
            foreach (var taskFlowGroupExecutionInstance in taskFlowStageExecutionInstance.TaskGroups)
            {
                if (taskFlowGroupExecutionInstance.IsTerminatedStatus())
                {
                    continue;
                }
                await ExecuteTaskGroupAsync(
                    taskFlowTemplate,
                    taskFlowExecutionInstance,
                    taskFlowStageExecutionInstance,
                    taskFlowGroupExecutionInstance,
                    cancellationToken);
            }
            if (taskFlowStageExecutionInstance.TaskGroups.Count == 0)
            {
                taskFlowStageExecutionInstance.Status = TaskFlowExecutionStatus.Finished;
            }
            else if (taskFlowStageExecutionInstance.TaskGroups.All(static x => x.Status == TaskFlowExecutionStatus.Finished))
            {
                taskFlowStageExecutionInstance.Status = TaskFlowExecutionStatus.Finished;
            }
            else if (taskFlowStageExecutionInstance.TaskGroups.Any(static x => x.Status != TaskFlowExecutionStatus.Finished))
            {
                if (taskFlowStageExecutionInstance.Status != TaskFlowExecutionStatus.Running
                    || (taskFlowStageExecutionInstance.Status == TaskFlowExecutionStatus.Running
                    && taskFlowStageExecutionInstance.RetryTasks))
                {
                    var taskFlowStageTemplate = taskFlowTemplate.Value.FindStageTemplate(taskFlowStageExecutionInstance.TaskFlowStageTemplateId);
                    if (taskFlowStageTemplate != null && taskFlowStageTemplate.ExecutionTimeLimitSeconds > 0)
                    {
                        await ScheduleTaskFlowStageExecutionTimeLimitAsync(
                            taskFlowExecutionInstance.Id,
                            taskFlowStageExecutionInstance.Id,
                            taskFlowStageTemplate.ExecutionTimeLimitSeconds,
                            cancellationToken);
                    }
                    taskFlowStageExecutionInstance.Status = TaskFlowExecutionStatus.Running;
                    taskFlowStageExecutionInstance.CreationDateTime = DateTime.UtcNow;
                }
            }
            else if (taskFlowStageExecutionInstance.TaskGroups.Any(static x => x.Status == TaskFlowExecutionStatus.Fault))
            {
                taskFlowStageExecutionInstance.Status = TaskFlowExecutionStatus.Fault;
            }
        }

        async ValueTask ScheduleTaskFlowStageExecutionTimeLimitAsync(
            string taskFlowExecutionInstanceId,
            string taskFlowStageExecutionInstanceId,
            int executionTimeLimitSeconds,
            CancellationToken cancellationToken = default)
        {
            var kafkaDelayMessage = new KafkaDelayMessage()
            {
                Type = nameof(TaskFlowExecutor),
                SubType = SubTyoe_TaskFlowStageExecutionTimeLimit,
                Id = taskFlowExecutionInstanceId,
                ScheduleDateTime = DateTime.UtcNow + TimeSpan.FromSeconds(executionTimeLimitSeconds),
                CreateDateTime = DateTime.UtcNow,
            };
            kafkaDelayMessage.Properties["StageId"] = taskFlowStageExecutionInstanceId;
            await _delayMessageQueue.EnqueueAsync(kafkaDelayMessage, cancellationToken);
        }

        async ValueTask ProcessTaskFlowStageExecutionTimeLimitReachedAsync(
            TaskFlowExecutionTimeLimitParameters parameters,
            CancellationToken cancellationToken)
        {
            var op = new AsyncOperation<Func<Task>>(async () =>
            {
                try
                {
                    await using var taskFlowRepo = await _taskFlowExecutionInstanceRepoFactory.CreateRepositoryAsync(cancellationToken);
                    var taskFlowExecutionInstance = await taskFlowRepo.GetByIdAsync(parameters.TaskFlowExecutionInstanceId, cancellationToken);
                    if (taskFlowExecutionInstance == null)
                    {
                        return;
                    }
                    var currentStage = taskFlowExecutionInstance.Value.TaskStages.ElementAtOrDefault(taskFlowExecutionInstance.Value.CurrentStageIndex);
                    if (currentStage == null)
                    {
                        return;
                    }
                    if (currentStage.Id != parameters.TaskFlowStageInstanceId)
                    {
                        return;
                    }
                    if (taskFlowExecutionInstance.Value.CurrentStageIndex < taskFlowExecutionInstance.Value.TaskStages.Count - 1)
                    {
                        taskFlowExecutionInstance.Value.CurrentStageIndex++;
                    }
                    taskFlowExecutionInstance.Value = taskFlowExecutionInstance.Value with { };
                    await this.ExecuteTaskFlowAsync(taskFlowExecutionInstance, cancellationToken);
                    taskFlowExecutionInstance.ModifiedDateTime = DateTime.UtcNow;
                    await taskFlowRepo.UpdateAsync(taskFlowExecutionInstance, cancellationToken);
                }
                catch (Exception ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }
                finally
                {

                }
            }, AsyncOperationKind.AddOrUpdate);
            await GetActionBlock(parameters.TaskFlowExecutionInstanceId).SendAsync(op, cancellationToken);
            await op.WaitAsync(cancellationToken);
        }

        async ValueTask ExecuteTaskGroupAsync(
            TaskFlowTemplateModel taskFlowTemplate,
            TaskFlowExecutionInstanceModel taskFlowExecutionInstance,
            TaskFlowStageExecutionInstance taskFlowStageExecutionInstance,
            TaskFlowGroupExecutionInstance taskFlowGroupExecutionInstance,
            CancellationToken cancellationToken = default)
        {
            if (taskFlowGroupExecutionInstance.IsTerminatedStatus())
            {
                return;
            }
            foreach (var taskFlowTaskExecutionInstance in taskFlowGroupExecutionInstance.Tasks)
            {
                await ExecuteTaskAsync(
                    taskFlowTemplate,
                    taskFlowExecutionInstance,
                    taskFlowStageExecutionInstance,
                    taskFlowGroupExecutionInstance,
                    taskFlowTaskExecutionInstance,
                    cancellationToken);
                if (taskFlowTaskExecutionInstance.Status == TaskExecutionStatus.Finished)
                {
                    continue;
                }
                break;
            }
            if (taskFlowGroupExecutionInstance.Tasks.Count == 0)
            {
                taskFlowGroupExecutionInstance.Status = TaskFlowExecutionStatus.Finished;
            }
            else if (taskFlowGroupExecutionInstance.Tasks.All(x => x.Status == TaskExecutionStatus.Finished))
            {
                taskFlowGroupExecutionInstance.Status = TaskFlowExecutionStatus.Finished;
            }
            else if (taskFlowGroupExecutionInstance.Tasks.Any(x => x.Status != TaskExecutionStatus.Finished))
            {
                if (taskFlowGroupExecutionInstance.Tasks.Any(x => x.Status == TaskExecutionStatus.Cancelled || x.Status == TaskExecutionStatus.Failed || x.Status == TaskExecutionStatus.PenddingTimeout))
                {
                    taskFlowGroupExecutionInstance.Status = TaskFlowExecutionStatus.Fault;
                }
                else
                {
                    taskFlowGroupExecutionInstance.Status = TaskFlowExecutionStatus.Running;
                }
            }
        }

        async ValueTask ExecuteTaskAsync(
            TaskFlowTemplateModel taskFlowTemplate,
            TaskFlowExecutionInstanceModel taskFlowExecutionInstance,
            TaskFlowStageExecutionInstance taskFlowStageExecutionInstance,
            TaskFlowGroupExecutionInstance taskFlowGroupExecutionInstance,
            TaskFlowTaskExecutionInstance taskFlowTaskExecutionInstance,
            CancellationToken cancellationToken = default)
        {
            var taskFlowStageTemplate = taskFlowTemplate.Value.FindStageTemplate(taskFlowStageExecutionInstance.TaskFlowStageTemplateId);
            if (taskFlowStageTemplate == null)
            {
                return;
            }
            var taskFlowGroupTemplate = taskFlowStageTemplate.FindGroupTemplate(taskFlowGroupExecutionInstance.TaskFlowGroupTemplateId);
            if (taskFlowGroupTemplate == null)
            {
                return;
            }
            var taskFlowTaskTemplate = taskFlowGroupTemplate.FindTaskTemplate(taskFlowTaskExecutionInstance.TaskFlowTaskTemplateId);
            if (taskFlowTaskTemplate == null)
            {
                return;
            }

            if (taskFlowTaskTemplate.TemplateType == TaskFlowTaskTemplateType.TriggerTask)
            {
                if (taskFlowTaskExecutionInstance.Status != TaskExecutionStatus.Finished)
                {
                    if (taskFlowTemplate.Value.ExecutionTimeLimitMinutes > 0)
                    {
                        await _delayMessageQueue.EnqueueAsync(new KafkaDelayMessage()
                        {
                            Type = nameof(TaskFlowExecutor),
                            SubType = SubType_ExecutionTimeLimit,
                            Id = taskFlowExecutionInstance.Id,
                            ScheduleDateTime = DateTime.UtcNow + TimeSpan.FromMinutes(taskFlowTemplate.Value.ExecutionTimeLimitMinutes)
                        }, cancellationToken);
                    }
                    taskFlowTaskExecutionInstance.Status = TaskExecutionStatus.Finished;
                }
            }
            else if (taskFlowTaskTemplate.TemplateType == TaskFlowTaskTemplateType.RemoteNodeTask)
            {

                switch (taskFlowTaskExecutionInstance.Status)
                {
                    case TaskExecutionStatus.Unknown:
                        {
                            var fireInstanceId = $"TaskFlow_{Guid.NewGuid()}";
                            await _taskActivateServiceParametersBatchQueue.SendAsync(new TaskActivateServiceParameters(new FireTaskParameters
                            {
                                FireTimeUtc = DateTime.UtcNow,
                                TriggerSource = TriggerSource.Manual,
                                TaskActivationRecordId = fireInstanceId,
                                TaskDefinitionId = taskFlowTaskExecutionInstance.TaskDefinitionId,
                                ScheduledFireTimeUtc = DateTime.UtcNow,
                                EnvironmentVariables = [.. taskFlowTemplate.Value.EnvironmentVariables],
                                TaskFlowTaskKey = new TaskFlowTaskKey(
                                     taskFlowExecutionInstance.Value.TaskFlowTemplateId,
                                     taskFlowExecutionInstance.Value.Id,
                                     taskFlowStageExecutionInstance.Id,
                                     taskFlowGroupExecutionInstance.Id,
                                     taskFlowTaskExecutionInstance.Id)
                            }), cancellationToken);
                        }

                        break;
                    case TaskExecutionStatus.PenddingTimeout:
                    case TaskExecutionStatus.Cancelled:
                    case TaskExecutionStatus.Failed:
                        //case TaskExecutionStatus.Running:
                        {
                            if (!taskFlowStageExecutionInstance.RetryTasks)
                            {
                                return;
                            }
                            var fireInstanceId = taskFlowTaskExecutionInstance.TaskActiveRecordId;
                            if (fireInstanceId == null)
                            {
                                return;
                            }
                            await _taskActivateServiceParametersBatchQueue.SendAsync(new TaskActivateServiceParameters(new FireTaskParameters
                            {
                                FireTimeUtc = DateTime.UtcNow,
                                TriggerSource = TriggerSource.Manual,
                                TaskActivationRecordId = fireInstanceId,
                                TaskDefinitionId = taskFlowTaskExecutionInstance.TaskDefinitionId,
                                ScheduledFireTimeUtc = DateTime.UtcNow,
                                EnvironmentVariables = [.. taskFlowTemplate.Value.EnvironmentVariables],
                                RetryTasks = true,
                                TaskFlowTaskKey = new TaskFlowTaskKey(
                                     taskFlowExecutionInstance.Value.TaskFlowTemplateId,
                                     taskFlowExecutionInstance.Value.Id,
                                     taskFlowStageExecutionInstance.Id,
                                     taskFlowGroupExecutionInstance.Id,
                                     taskFlowTaskExecutionInstance.Id)
                            }), cancellationToken);
                        }

                        break;
                    case TaskExecutionStatus.Finished:
                        break;
                    default:
                        break;
                }
            }

            await ValueTask.CompletedTask;
        }

    }
}
