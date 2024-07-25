using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Models;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Threading;

namespace NodeService.WebServer.Services.Tasks
{

    public class TaskFlowExecutor
    {
        readonly ILogger<TaskFlowExecutor> _logger;
        readonly ExceptionCounter _exceptionCounter;
        readonly BatchQueue<TaskActivateServiceParameters> _taskActivateServiceParametersBatchQueue;
        readonly ApplicationRepositoryFactory<TaskFlowTemplateModel> _taskFlowTemplateRepoFactory;
        readonly ApplicationRepositoryFactory<TaskFlowExecutionInstanceModel> _taskFlowExecutionInstanceRepoFactory;
        readonly ActionBlock<AsyncOperation<Func<Task>>> _executionQueue;

        public TaskFlowExecutor(
            ILogger<TaskFlowExecutor> logger,
            ExceptionCounter exceptionCounter,
            BatchQueue<TaskActivateServiceParameters> taskActivateServiceBatchQueue,
            ApplicationRepositoryFactory<TaskFlowTemplateModel> taskFlowTemplateRepoFactory,
            ApplicationRepositoryFactory<TaskFlowExecutionInstanceModel> taskFlowExecutionInstanceRepoFactory
            )
        {
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _taskActivateServiceParametersBatchQueue = taskActivateServiceBatchQueue;
            _taskFlowTemplateRepoFactory = taskFlowTemplateRepoFactory;
            _taskFlowExecutionInstanceRepoFactory = taskFlowExecutionInstanceRepoFactory;
            _executionQueue = new ActionBlock<AsyncOperation<Func<Task>>>(ExecutionTaskAsync, new ExecutionDataflowBlockOptions()
            {
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
        }

        async Task ExecutionTaskAsync(AsyncOperation<Func<Task>> op)
        {
            await op.Argument.Invoke();
            op.TrySetResult();
        }

        public async ValueTask ExecuteAsync(
            TaskFlowExecutionInstanceModel taskFlowExecutionInstance,
            CancellationToken cancellationToken = default)
        {
            var op = new AsyncOperation<Func<Task>>(async () =>
            {
                await ExecuteTaskFlowAsync(taskFlowExecutionInstance);
            }, AsyncOperationKind.AddOrUpdate);
            await _executionQueue.SendAsync(op, cancellationToken);
            await op.WaitAsync(cancellationToken);
        }

        async ValueTask ExecuteTaskFlowAsync(
            TaskFlowExecutionInstanceModel taskFlowExecutionInstance,
            CancellationToken cancellationToken = default)
        {
            if (taskFlowExecutionInstance.Value.IsTerminatedStatus())
            {
                return;
            }
            await using var taskFlowTemplateRepo = await _taskFlowTemplateRepoFactory.CreateRepositoryAsync();
            var taskFlowTemplate = await taskFlowTemplateRepo.GetByIdAsync(taskFlowExecutionInstance.Value.TaskFlowTemplateId, cancellationToken);
            if (taskFlowTemplate == null)
            {
                return;
            }
            do
            {
                var taskStageExecutionInstance = taskFlowExecutionInstance.TaskStages.ElementAtOrDefault(taskFlowExecutionInstance.Value.CurrentStageIndex);
                if (taskStageExecutionInstance == null)
                {
                    break;
                }
                var taskStageTemplate = taskFlowTemplate.Value.TaskStages.FirstOrDefault(x => x.Id == taskStageExecutionInstance.TaskFlowStageTemplateId);
                if (taskStageExecutionInstance.IsTerminatedStatus())
                {
                    taskFlowExecutionInstance.Value.CurrentStageIndex++;
                    continue;
                }
                await ExecuteTaskStageAsync(
                            taskFlowTemplate,
                            taskFlowExecutionInstance,
                            taskStageExecutionInstance,
                            cancellationToken);
                if (taskStageExecutionInstance.Status == TaskFlowExecutionStatus.Finished)
                {
                    taskFlowExecutionInstance.Value.CurrentStageIndex++;
                    continue;
                }
                break;
            } while (true);

            if (taskFlowExecutionInstance.TaskStages.All(x => x.Status == TaskFlowExecutionStatus.Finished))
            {
                taskFlowExecutionInstance.Value.Status = TaskFlowExecutionStatus.Finished;
            }
            else if (taskFlowExecutionInstance.Value.TaskStages.Any(x => x.Status != TaskFlowExecutionStatus.Finished))
            {
                taskFlowExecutionInstance.Value.Status = TaskFlowExecutionStatus.Running;
            }

        }
        async ValueTask ExecuteTaskStageAsync(
            TaskFlowTemplateModel taskFlowTemplate,
            TaskFlowExecutionInstanceModel taskFlowExecutionInstance,
            TaskFlowStageExecutionInstance taskFlowStageExecutionInstance,
            CancellationToken cancellationToken = default)
        {
            foreach (var taskGroupExecutionInstance in taskFlowStageExecutionInstance.TaskGroups)
            {
                if (taskGroupExecutionInstance.IsTerminatedStatus())
                {
                    continue;
                }
                await ExecuteTaskGroupAsync(
                    taskFlowTemplate,
                    taskFlowExecutionInstance,
                    taskFlowStageExecutionInstance,
                    taskGroupExecutionInstance,
                    cancellationToken);
            }
            if (taskFlowStageExecutionInstance.TaskGroups.Count == 0)
            {
                taskFlowStageExecutionInstance.Status = TaskFlowExecutionStatus.Finished;
            }
            else if (taskFlowStageExecutionInstance.TaskGroups.All(x => x.Status == TaskFlowExecutionStatus.Finished))
            {
                taskFlowStageExecutionInstance.Status = TaskFlowExecutionStatus.Finished;
            }
            else if (taskFlowStageExecutionInstance.TaskGroups.Any(x => x.Status != TaskFlowExecutionStatus.Finished))
            {
                if (taskFlowStageExecutionInstance.Status != TaskFlowExecutionStatus.Running)
                {
                    var stageTemplate = taskFlowTemplate.Value.FindStageTemplate(taskFlowStageExecutionInstance.TaskFlowStageTemplateId);
                    if (stageTemplate != null && stageTemplate.ExecutionTimeLimitSeconds > 0)
                    {
                        var prameters = new TaskFlowExecutionTimeLimitParameters(
                            taskFlowExecutionInstance.Id,
                            taskFlowStageExecutionInstance.Id);
                        prameters.Disposable = Scheduler.Default.ScheduleAsync(
                            prameters,
                            TimeSpan.FromSeconds(stageTemplate.ExecutionTimeLimitSeconds),
                            ExecutionTimeLimitReachedAsync);
                    }
                    taskFlowStageExecutionInstance.Status = TaskFlowExecutionStatus.Running;
                    taskFlowStageExecutionInstance.CreationDateTime = DateTime.UtcNow;
                }
            }
        }

        async Task<IDisposable> ExecutionTimeLimitReachedAsync(
            System.Reactive.Concurrency.IScheduler scheduler,
            TaskFlowExecutionTimeLimitParameters parameters,
            CancellationToken cancellationToken)
        {
            var op = new AsyncOperation<Func<Task>>(async () =>
            {
                try
                {
                    await using var taskFlowRepo = await _taskFlowExecutionInstanceRepoFactory.CreateRepositoryAsync(cancellationToken);
                    var taskFlowExcutionInstance = await taskFlowRepo.GetByIdAsync(parameters.TaskFlowInstanceId, cancellationToken);
                    if (taskFlowExcutionInstance == null)
                    {
                        return;
                    }
                    taskFlowExcutionInstance.Value.CurrentStageIndex++;
                    taskFlowExcutionInstance.Value.CurrentStageIndex = Math.Min(taskFlowExcutionInstance.Value.TaskStages.Count - 1, taskFlowExcutionInstance.Value.CurrentStageIndex);
                    await taskFlowRepo.SaveChangesAsync(cancellationToken);
                    await this.ExecuteTaskFlowAsync(taskFlowExcutionInstance, cancellationToken);
                }
                catch (Exception ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }
                finally
                {
                    parameters.Disposable.Dispose();
                }
            }, AsyncOperationKind.AddOrUpdate);
            await _executionQueue.SendAsync(op, cancellationToken);
            await op.WaitAsync(cancellationToken);
            return Disposable.Empty;
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
                taskFlowGroupExecutionInstance.Status = TaskFlowExecutionStatus.Running;
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
            var taskFlowTaskTemplate = taskFlowTemplate
                  .Value.FindStageTemplate(taskFlowStageExecutionInstance.TaskFlowStageTemplateId)
                  ?.FindGroupTemplate(taskFlowGroupExecutionInstance.TaskFlowGroupTemplateId)
                  ?.FindTaskTemplate(taskFlowTaskExecutionInstance.TaskFlowTaskTemplateId);
            if (taskFlowTaskTemplate==null)
            {
                return;
            }
            if (taskFlowTaskTemplate.TemplateType == TaskFlowTaskTemplateType.TriggerTask)
            {
                taskFlowTaskExecutionInstance.Status = TaskExecutionStatus.Finished;
            }
            else if (taskFlowTaskTemplate.TemplateType == TaskFlowTaskTemplateType.RemoteNodeTask)
            {
                switch (taskFlowTaskExecutionInstance.Status)
                {
                    case TaskExecutionStatus.Unknown:
                        var fireInstanceId = $"TaskFlow_{Guid.NewGuid()}";
                        await _taskActivateServiceParametersBatchQueue.SendAsync(new TaskActivateServiceParameters(new FireTaskParameters
                        {
                            FireTimeUtc = DateTime.UtcNow,
                            TriggerSource = TriggerSource.Manual,
                            FireInstanceId = fireInstanceId,
                            TaskDefinitionId = taskFlowTaskExecutionInstance.TaskDefinitionId,
                            ScheduledFireTimeUtc = DateTime.UtcNow,
                            TaskFlowTaskKey = new TaskFlowTaskKey(
                                 taskFlowExecutionInstance.Value.TaskFlowTemplateId,
                                 taskFlowExecutionInstance.Value.Id,
                                 taskFlowStageExecutionInstance.Id,
                                 taskFlowGroupExecutionInstance.Id,
                                 taskFlowTaskExecutionInstance.Id)
                        }), cancellationToken);

                        break;
                    case TaskExecutionStatus.Running:
                        break;
                    case TaskExecutionStatus.Finished:
                        break;
                        break;
                    default:
                        break;
                }
            }

            await ValueTask.CompletedTask;
        }

    }
}
