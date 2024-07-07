using Microsoft.Extensions.DependencyInjection;
using NodeService.WebServer.Data.Repositories.Specifications;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.Tasks
{
    public partial class TaskScheduleService
    {
        async ValueTask ProcessTaskFlowScheduleParametersAsync(BatchQueueOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult> op, CancellationToken cancellationToken)
        {
            switch (op.Kind)
            {
                case BatchQueueOperationKind.None:
                    break;
                case BatchQueueOperationKind.AddOrUpdate:
                    await ScheduleTaskFlowAsync(op.Argument.Parameters.AsT1, cancellationToken);
                    break;
                case BatchQueueOperationKind.Delete:
                    await DeleteAllTaskScheduleAsync(op.Argument.Parameters.AsT1.TaskFlowTemplateId);
                    break;
                case BatchQueueOperationKind.Query:
                    break;
                default:
                    break;
            }
        }


        async ValueTask ScheduleTaskFlowAsync(
    TaskFlowScheduleParameters taskFlowScheduleParameters,
    CancellationToken cancellationToken = default)
        {
            if (taskFlowScheduleParameters.TaskFlowTemplateId == null) return;
            using var repository = _taskFlowTemplateRepoFactory.CreateRepository();
            var taskFlowTemplate = await repository.GetByIdAsync(taskFlowScheduleParameters.TaskFlowTemplateId, cancellationToken);

            if (taskFlowTemplate == null)
            {
                await DeleteAllTaskFlowScheduleAsync(taskFlowScheduleParameters.TaskFlowTemplateId);
                return;
            }
            else
            {
                var triggerTask = taskFlowTemplate.GetTriggerTask();
                if (triggerTask == null || triggerTask.TriggerType != TaskTriggerType.Schedule)
                {
                    await DeleteAllTaskFlowScheduleAsync(taskFlowScheduleParameters.TaskFlowTemplateId);
                    return;
                }
            }

            var taskSchedulerKey = new TaskSchedulerKey(
                taskFlowScheduleParameters.TaskFlowTemplateId,
                taskFlowScheduleParameters.TriggerSource,
                nameof(FireTaskFlowJob));

            if (_taskSchedulerDictionary.TryGetValue(taskSchedulerKey, out var asyncDisposable))
            {
                await asyncDisposable.DisposeAsync();
                if (!taskFlowTemplate.Value.IsDesignMode)
                {
                    await DeleteAllTaskScheduleAsync(taskFlowScheduleParameters.TaskFlowTemplateId);
                    return;
                }

                var newAsyncDisposable = await ScheduleTaskFlowAsync(
                                                                            taskSchedulerKey,
                                                                            taskFlowTemplate,
                                                                            cancellationToken);
                _taskSchedulerDictionary.TryUpdate(taskSchedulerKey, newAsyncDisposable, asyncDisposable);
            }
            else
            {
                asyncDisposable = await ScheduleTaskFlowAsync(
                                                                taskSchedulerKey,
                                                                taskFlowTemplate,
                                                                cancellationToken);
                _taskSchedulerDictionary.TryAdd(taskSchedulerKey, asyncDisposable);
            }
        }

        private async ValueTask DeleteAllTaskFlowScheduleAsync(string key)
        {
            for (var triggerSource = TriggerSource.Schedule; triggerSource < TriggerSource.Max - 1; triggerSource++)
            {
                var taskSchedulerKey = new TaskSchedulerKey(key, triggerSource, nameof(FireTaskFlowJob));
                if (_taskSchedulerDictionary.TryRemove(taskSchedulerKey, out var asyncDisposable))
                    await asyncDisposable.DisposeAsync();
            }
        }

        private async ValueTask ScheduleTaskFlowsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var taskFlowTemplateRepo = _taskFlowTemplateRepoFactory.CreateRepository();
                var taskFlowTemplates = await taskFlowTemplateRepo.ListAsync(cancellationToken);
                foreach (var taskFlowTemplate in taskFlowTemplates)
                {
                    var triggerTask = taskFlowTemplate.GetTriggerTask();
                    if (triggerTask==null)
                    {
                        continue;
                    }
                    if (triggerTask.TriggerType != TaskTriggerType.Schedule)
                    {
                        continue;
                    }
                    var taskFlowScheduleParameters = new TaskFlowScheduleParameters(TriggerSource.Schedule, taskFlowTemplate.Id);
                    var taskScheduleServiceParameters = new TaskScheduleServiceParameters(taskFlowScheduleParameters);
                    var op = new BatchQueueOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>(
                        taskScheduleServiceParameters,
                        BatchQueueOperationKind.AddOrUpdate);
                    await _taskScheduleServiceParametersQueue.EnqueueAsync(op, cancellationToken);
                }

            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
        }

        async ValueTask<IAsyncDisposable> ScheduleTaskFlowAsync(
            TaskSchedulerKey taskSchedulerKey,
            TaskFlowTemplateModel  taskFlowTemplate,
            CancellationToken cancellationToken = default)
        {
            var triggerTaskTemplate = taskFlowTemplate.GetTriggerTask();
            var asyncDisposable = await _jobScheduler.ScheduleAsync<FireTaskFlowJob>(taskSchedulerKey,
                TriggerBuilderHelper.BuildScheduleTrigger(triggerTaskTemplate.TriggerSources.Select(x => x.Value)),
                new Dictionary<string, object?>
                {
                    {
                        nameof(TaskFlowTemplateModel.Id),
                        taskFlowTemplate.Id
                    }
                },
                cancellationToken
            );
            return asyncDisposable;
        }
    }
}
