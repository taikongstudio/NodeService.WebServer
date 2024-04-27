namespace NodeService.WebServer.Services.Tasks
{
    public class TaskPenddingContext : IAsyncDisposable
    {
        private CancellationTokenSource _cancelTokenSource;

        public TaskPenddingContext(string id)
        {
            Id = id;
        }

        public string Id { get; }

        public INodeSessionService NodeServerService { get; init; }

        public required NodeSessionId NodeSessionId { get; init; }

        public required JobExecutionEventRequest FireEvent { get; init; }

        public CancellationToken CancellationToken { get; private set; }

        public required FireTaskParameters FireParameters { get; init; }

        public async Task<bool> WaitAllTaskTerminatedAsync()
        {
            while (!CancellationToken.IsCancellationRequested)
            {
                if (NodeServerService.GetNodeStatus(NodeSessionId) != NodeStatus.Online)
                {
                    return false;
                }

                var jobExecutionInstances = await GetRunningTasksAsync();

                if (!jobExecutionInstances.Any())
                {
                    return true;
                }
                await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken);
            }
            return false;
        }

        public async Task<IEnumerable<JobExecutionInstanceModel>> GetRunningTasksAsync()
        {
            var taskExecutionInstances = await NodeServerService.QueryTaskExecutionInstancesAsync(NodeSessionId.NodeId
                , new QueryTaskExecutionInstancesParameters()
                {
                    Id = FireParameters.TaskScheduleConfig.Id,
                    Status = JobExecutionStatus.Running,
                }, CancellationToken);
            return taskExecutionInstances;
        }

        public async Task<bool> StopRunningTasksAsync()
        {
            while (!CancellationToken.IsCancellationRequested)
            {
                var taskExecutionInstances = await GetRunningTasksAsync();

                int count = taskExecutionInstances.Count();


                if (count == 0)
                {
                    return true;
                }
                foreach (var taskExecutionInstance in taskExecutionInstances)
                {
                    await NodeServerService.SendJobExecutionEventAsync(
                        NodeSessionId,
                        taskExecutionInstance.ToCancelEvent(),
                        CancellationToken);
                }
                await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken);
            }
            return false;
        }

        public ValueTask EnsureInitAsync()
        {
            if (_cancelTokenSource == null)
            {
                _cancelTokenSource = new CancellationTokenSource();
                _cancelTokenSource.CancelAfter(TimeSpan.FromSeconds(Math.Max(30, FireParameters.TaskScheduleConfig.PenddingLimitTimeSeconds)));
                CancellationToken = _cancelTokenSource.Token;
            }
            return ValueTask.CompletedTask;
        }

        public async Task<bool> WaitForRunningTasksAsync()
        {
            while (true)
            {
                var runningTasks = await this.GetRunningTasksAsync();
                if (!runningTasks.Any())
                {
                    break;
                }
                await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken);
            }
            return true;
        }

        public async ValueTask CancelAsync()
        {
            if (!_cancelTokenSource.IsCancellationRequested)
            {
                await _cancelTokenSource.CancelAsync();
            }
        }

        public ValueTask DisposeAsync()
        {
            _cancelTokenSource.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
