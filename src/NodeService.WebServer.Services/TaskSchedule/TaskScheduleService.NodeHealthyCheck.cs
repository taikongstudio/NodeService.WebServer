using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.TaskSchedule.Jobs;

namespace NodeService.WebServer.Services.TaskSchedule
{
    public partial class TaskScheduleService
    {

        private async ValueTask ProcessNodeHealthyCheckScheduleParametersAsync(
            AsyncOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult> op,
            CancellationToken cancellationToken)
        {
            switch (op.Kind)
            {
                case AsyncOperationKind.None:
                    break;
                case AsyncOperationKind.AddOrUpdate:
                    await ScheduleNodeHealthyCheckAsync(
                        op.Argument.Parameters.AsT2,
                        cancellationToken);
                    break;
                case AsyncOperationKind.Delete:
                    await DeleteAllTaskScheduleAsync(
                        op.Argument.Parameters.AsT2.ConfigurationId,
                        nameof(FireNodeHeathyCheckJob));
                    break;
                case AsyncOperationKind.Query:
                    break;
                default:
                    break;
            }
        }

        async ValueTask ScheduleNodeHealthyCheckAsync(
            NodeHealthyCheckScheduleParameters parameters,
            CancellationToken cancellationToken)
        {
            if (parameters.ConfigurationId == null) return;
            await using var repository = await _propertyBagRepoFactory.CreateRepositoryAsync(cancellationToken);
            var propertyBag =await repository.FirstOrDefaultAsync(new PropertyBagSpecification(parameters.ConfigurationId), cancellationToken);
            var nodeHealthyCheckConfiguration = JsonSerializer.Deserialize<NodeHealthyCheckConfiguration>(propertyBag["Value"] as string);
            if (nodeHealthyCheckConfiguration == null || !nodeHealthyCheckConfiguration.IsEnabled)
            {
                await DeleteAllTaskScheduleAsync(parameters.ConfigurationId, nameof(FireNodeHeathyCheckJob));
                return;
            }
            if (nodeHealthyCheckConfiguration.DailyTimes.Count == 0)
            {
                await DeleteAllTaskScheduleAsync(parameters.ConfigurationId, nameof(FireNodeHeathyCheckJob));
                return;
            }

            var taskSchedulerKey = new TaskSchedulerKey(
                parameters.ConfigurationId,
                TriggerSource.Schedule,
                nameof(FireNodeHeathyCheckJob));

            if (_taskSchedulerDictionary.TryGetValue(taskSchedulerKey, out var asyncDisposable))
            {
                await asyncDisposable.DisposeAsync();
                var newAsyncDisposable = await ScheduleNodeHealthyCheckAsync(
                    taskSchedulerKey,
                    nodeHealthyCheckConfiguration,
                    cancellationToken);
                _taskSchedulerDictionary.TryUpdate(taskSchedulerKey, newAsyncDisposable, asyncDisposable);
            }
            else
            {
                asyncDisposable = await ScheduleNodeHealthyCheckAsync(
                    taskSchedulerKey,
                    nodeHealthyCheckConfiguration,
                    cancellationToken);
                _taskSchedulerDictionary.TryAdd(taskSchedulerKey, asyncDisposable);
            }
        }

        async ValueTask<IAsyncDisposable> ScheduleNodeHealthyCheckAsync(
            TaskSchedulerKey taskSchedulerKey,
            NodeHealthyCheckConfiguration configuration,
            CancellationToken cancellationToken = default)
        {

            var triggers = configuration.DailyTimes.Select(TimeOnlyToTrigger).ToArray().AsReadOnly();
            var asyncDisposable = await _jobScheduler.ScheduleAsync<FireNodeHeathyCheckJob>(taskSchedulerKey,
                triggers,
                new Dictionary<string, object?>() {
                    {
                            "Id",
                            taskSchedulerKey.Key
                    },
                    {
                            "DateTime",
                            DateTime.UtcNow
                    }
                },
                cancellationToken
            );
            _webServerCounter.Snapshot.NodeHeathyCheckScheduleCount.Value++;
            return asyncDisposable;
        }

        private async ValueTask ScheduleNodeHealthyCheckAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var taskScheduleServiceParameters = new TaskScheduleServiceParameters(new NodeHealthyCheckScheduleParameters()
                {
                    ConfigurationId = nameof(NotificationSources.NodeHealthyCheck)
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
