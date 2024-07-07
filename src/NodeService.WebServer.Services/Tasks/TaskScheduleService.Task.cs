using Microsoft.Extensions.DependencyInjection;
using NodeService.WebServer.Data.Repositories.Specifications;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.Tasks
{
    public partial class TaskScheduleService
    {
        async Task ProcessTaskScheduleParametersAsync(
        BatchQueueOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult> op,
        CancellationToken cancellationToken = default)
        {
            switch (op.Kind)
            {
                case BatchQueueOperationKind.None:
                    break;
                case BatchQueueOperationKind.AddOrUpdate:
                    await ScheduleTaskAsync(op.Argument.Parameters.AsT0, cancellationToken);
                    break;
                case BatchQueueOperationKind.Delete:
                    await DeleteAllTaskScheduleAsync(op.Argument.Parameters.AsT0.TaskDefinitionId);
                    break;
                case BatchQueueOperationKind.Query:
                    break;
                default:
                    break;
            }
        }

        private async ValueTask ScheduleTaskAsync(
            TaskScheduleParameters taskScheduleParameters,
            CancellationToken cancellationToken = default)
        {
            if (taskScheduleParameters.TaskDefinitionId == null) return;
            using var repository = _taskDefinitionRepositoryFactory.CreateRepository();
            var taskDefinition = await repository.GetByIdAsync(taskScheduleParameters.TaskDefinitionId, cancellationToken);
            if (taskDefinition == null || !taskDefinition.Value.IsEnabled || taskDefinition.Value.TriggerType != TaskTriggerType.Schedule)
            {
                await DeleteAllTaskScheduleAsync(taskScheduleParameters.TaskDefinitionId);
                return;
            }



            var taskSchedulerKey = new TaskSchedulerKey(
                taskScheduleParameters.TaskDefinitionId,
                taskScheduleParameters.TriggerSource,
                nameof(FireTaskJob));

            if (_taskSchedulerDictionary.TryGetValue(taskSchedulerKey, out var asyncDisposable))
            {
                await asyncDisposable.DisposeAsync();
                if (!taskDefinition.IsEnabled)
                {
                    await DeleteAllTaskScheduleAsync(taskScheduleParameters.TaskDefinitionId);
                    return;
                }

                var newAsyncDisposable = await ScheduleTaskAsync(
                    taskSchedulerKey,
                    taskDefinition,
                    cancellationToken);
                _taskSchedulerDictionary.TryUpdate(taskSchedulerKey, newAsyncDisposable, asyncDisposable);
            }
            else
            {
                asyncDisposable = await ScheduleTaskAsync(
                    taskSchedulerKey,
                    taskDefinition,
                    cancellationToken);
                _taskSchedulerDictionary.TryAdd(taskSchedulerKey, asyncDisposable);
            }
        }

        private async ValueTask DeleteAllTaskScheduleAsync(string key)
        {
            for (var triggerSource = TriggerSource.Schedule; triggerSource < TriggerSource.Max - 1; triggerSource++)
            {
                var taskSchedulerKey = new TaskSchedulerKey(key, triggerSource, nameof(FireTaskJob));
                if (_taskSchedulerDictionary.TryRemove(taskSchedulerKey, out var asyncDisposable))
                    await asyncDisposable.DisposeAsync();
            }
        }

        private async ValueTask ScheduleTasksAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var repository = _taskDefinitionRepositoryFactory.CreateRepository();
                var taskDefinitions =
                    await repository.ListAsync(new TaskDefinitionSpecification(true, TaskTriggerType.Schedule));
                taskDefinitions = taskDefinitions.Where(x => x.IsEnabled && x.TriggerType == TaskTriggerType.Schedule)
                    .ToList();
                foreach (var taskDefinition in taskDefinitions)
                {
                    var taskScheduleParameters = new TaskScheduleParameters(TriggerSource.Schedule, taskDefinition.Id);
                    var taskScheduleServiceParameters = new TaskScheduleServiceParameters(taskScheduleParameters);
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

        private async ValueTask<IAsyncDisposable> ScheduleTaskAsync(
            TaskSchedulerKey taskSchedulerKey,
            TaskDefinitionModel taskDefinition,
            CancellationToken cancellationToken = default)
        {
            var asyncDisposable = await _jobScheduler.ScheduleAsync<FireTaskJob>(taskSchedulerKey,
                TriggerBuilderHelper.BuildScheduleTrigger(taskDefinition.CronExpressions.Select(x => x.Value)),
                new Dictionary<string, object?>
                {
                    {
                        nameof(TaskDefinitionModel.Id),
                        taskDefinition.Id
                    }
                },
                cancellationToken
            );
            return asyncDisposable;
        }
    }
}
