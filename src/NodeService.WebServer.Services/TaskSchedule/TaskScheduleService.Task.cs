using Microsoft.Extensions.DependencyInjection;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Tasks;
using NodeService.WebServer.Services.TaskSchedule.Jobs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.TaskSchedule
{
    public partial class TaskScheduleService
    {
        async ValueTask ProcessTaskDefinitionScheduleParametersAsync(
        AsyncOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult> op,
        CancellationToken cancellationToken = default)
        {
            switch (op.Kind)
            {
                case AsyncOperationKind.None:
                    break;
                case AsyncOperationKind.AddOrUpdate:
                    await ScheduleTaskDefinitionAsync(op.Argument.Parameters.AsT0, cancellationToken);
                    break;
                case AsyncOperationKind.Delete:
                    await DeleteAllTaskScheduleAsync(op.Argument.Parameters.AsT0.TaskDefinitionId, nameof(FireTaskJob));
                    break;
                case AsyncOperationKind.Query:
                    break;
                default:
                    break;
            }
        }

        private async ValueTask ScheduleTaskDefinitionAsync(
            TaskDefinitionScheduleParameters taskScheduleParameters,
            CancellationToken cancellationToken = default)
        {
            if (taskScheduleParameters.TaskDefinitionId == null) return;
            await using var repository = await _taskDefinitionRepositoryFactory.CreateRepositoryAsync();
            var taskDefinition = await repository.GetByIdAsync(taskScheduleParameters.TaskDefinitionId, cancellationToken);
            if (taskDefinition == null || !taskDefinition.Value.IsEnabled || taskDefinition.Value.TriggerType != TaskTriggerType.Schedule)
            {
                await DeleteAllTaskScheduleAsync(taskScheduleParameters.TaskDefinitionId, nameof(FireTaskJob));
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
                    await DeleteAllTaskScheduleAsync(taskScheduleParameters.TaskDefinitionId, nameof(FireTaskJob));
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

        private async ValueTask ScheduleTaskDefinitionsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await using var repository = await _taskDefinitionRepositoryFactory.CreateRepositoryAsync();
                var taskDefinitions = await repository.ListAsync(new ListSpecification<TaskDefinitionModel>(), cancellationToken);
                taskDefinitions = taskDefinitions.Where(x => x.IsEnabled && x.TriggerType == TaskTriggerType.Schedule)
                    .ToList();
                foreach (var taskDefinition in taskDefinitions)
                {
                    var taskScheduleParameters = new TaskDefinitionScheduleParameters(TriggerSource.Schedule, taskDefinition.Id);
                    var taskScheduleServiceParameters = new TaskScheduleServiceParameters(taskScheduleParameters);
                    var op = new AsyncOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>(
                        taskScheduleServiceParameters,
                        AsyncOperationKind.AddOrUpdate);
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
                    },
                    {
                        nameof(FireTaskParameters.ParentTaskExecutionInstanceId),
                        taskDefinition.Id
                    }
                },
                cancellationToken
            );
            return asyncDisposable;
        }
    }
}
