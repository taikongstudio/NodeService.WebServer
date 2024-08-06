using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
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

        private async ValueTask ProcessTaskObservationScheduleParametersAsync(
            AsyncOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult> op,
            CancellationToken cancellationToken)
        {
            switch (op.Kind)
            {
                case AsyncOperationKind.None:
                    break;
                case AsyncOperationKind.AddOrUpdate:
                    await ScheduleTaskObservationAsync(
                        op.Argument.Parameters.AsT3,
                        cancellationToken);
                    break;
                case AsyncOperationKind.Delete:
                    await DeleteAllTaskScheduleAsync(
                        op.Argument.Parameters.AsT3.ConfigurationId,
                        nameof(FireTaskObservationJob));
                    break;
                case AsyncOperationKind.Query:
                    break;
                default:
                    break;
            }
        }

        async ValueTask ScheduleTaskObservationAsync(
            TaskObservationScheduleParameters parameters,
            CancellationToken cancellationToken)
        {
            if (parameters.ConfigurationId == null) return;
            await using var repository = await _propertyBagRepoFactory.CreateRepositoryAsync(cancellationToken);
            var propertyBag =await repository.FirstOrDefaultAsync(new PropertyBagSpecification(parameters.ConfigurationId), cancellationToken);
            var taskObservationConfiguration = JsonSerializer.Deserialize<TaskObservationConfiguration>(propertyBag["Value"] as string);
            if (taskObservationConfiguration == null || !taskObservationConfiguration.IsEnabled)
            {
                await DeleteAllTaskScheduleAsync(parameters.ConfigurationId, nameof(FireTaskObservationJob));
                return;
            }
            if (taskObservationConfiguration.DailyTimes.Count == 0)
            {
                await DeleteAllTaskScheduleAsync(parameters.ConfigurationId, nameof(FireTaskObservationJob));
                return;
            }

            var taskSchedulerKey = new TaskSchedulerKey(
                parameters.ConfigurationId,
                TriggerSource.Schedule,
                nameof(FireTaskObservationJob));

            if (_taskSchedulerDictionary.TryGetValue(taskSchedulerKey, out var asyncDisposable))
            {
                await asyncDisposable.DisposeAsync();
                if (!taskObservationConfiguration.IsEnabled)
                {
                    await DeleteAllTaskScheduleAsync(parameters.ConfigurationId, nameof(FireTaskObservationJob));
                    return;
                }

                var newAsyncDisposable = await ScheduleTaskObservationAsync(
                    taskSchedulerKey,
                    taskObservationConfiguration,
                    cancellationToken);
                _taskSchedulerDictionary.TryUpdate(taskSchedulerKey, newAsyncDisposable, asyncDisposable);
            }
            else
            {
                asyncDisposable = await ScheduleTaskObservationAsync(
                    taskSchedulerKey,
                    taskObservationConfiguration,
                    cancellationToken);
                _taskSchedulerDictionary.TryAdd(taskSchedulerKey, asyncDisposable);
            }
        }

        async ValueTask<IAsyncDisposable> ScheduleTaskObservationAsync(
            TaskSchedulerKey taskSchedulerKey,
            TaskObservationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            var triggers = configuration.DailyTimes.Select(TimeOnlyToTrigger).ToArray().AsReadOnly();
            var asyncDisposable = await _jobScheduler.ScheduleAsync<FireTaskObservationJob>(taskSchedulerKey,
                triggers,
                new Dictionary<string, object?>() { { "Id", taskSchedulerKey.Key } },
                cancellationToken
            );
            return asyncDisposable;
        }

        private async ValueTask ScheduleTaskObservationAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var taskScheduleServiceParameters = new TaskScheduleServiceParameters(new NodeHealthyCheckScheduleParameters()
                {
                    ConfigurationId = nameof(NotificationSources.TaskObservation)
                });
                var op = new AsyncOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>(
                    taskScheduleServiceParameters,
                    AsyncOperationKind.AddOrUpdate);
                await _taskScheduleServiceParametersQueue.EnqueueAsync(op, cancellationToken);

            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
        }

    }
}
