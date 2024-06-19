using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories.Specifications;
using System.Net;

namespace NodeService.WebServer.Services.Tasks;

public class TaskPenddingContext : IAsyncDisposable
{
    private CancellationTokenSource _cancelTokenSource;

    public TaskPenddingContext(string id)
    {
        Id = id;
    }

    public string Id { get; }

    public INodeSessionService NodeSessionService { get; init; }

    public required NodeSessionId NodeSessionId { get; init; }

    public required TaskDefinitionModel TaskDefinition { get; init; }

    public required TaskExecutionEventRequest TriggerEvent { get; init; }

    public CancellationToken CancellationToken { get; private set; }

    public required FireTaskParameters FireParameters { get; init; }

    public ValueTask DisposeAsync()
    {
        _cancelTokenSource.Dispose();
        return ValueTask.CompletedTask;
    }

    public async Task<bool> WaitAllTaskTerminatedAsync(IRepository<TaskExecutionInstanceModel> repository)
    {
        while (!CancellationToken.IsCancellationRequested)
        {
            if (NodeSessionService.GetNodeStatus(NodeSessionId) != NodeStatus.Online) return false;

            var queryResult = await QueryTaskExecutionInstancesAsync(repository,
                new QueryTaskExecutionInstanceListParameters
                {
                    NodeIdList = [NodeSessionId.NodeId.Value],
                    TaskDefinitionIdList = [TaskDefinition.Id],
                    Status = TaskExecutionStatus.Running
                });

            if (queryResult.IsEmpty) return true;
            await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken);
        }

        return false;
    }

    public async Task<bool> StopRunningTasksAsync(IRepository<TaskExecutionInstanceModel> repository, BatchQueue<TaskCancellationParameters> taskCancellationQueue)
    {
        while (!CancellationToken.IsCancellationRequested)
        {
            var queryResult = await QueryTaskExecutionInstancesAsync(repository,
                new QueryTaskExecutionInstanceListParameters
                {
                    NodeIdList = [NodeSessionId.NodeId.Value],
                    TaskDefinitionIdList = [TaskDefinition.Id],
                    Status = TaskExecutionStatus.Running
                });

            if (queryResult.IsEmpty) return true;
            foreach (var taskExecutionInstance in queryResult.Items)
            {
                await taskCancellationQueue.SendAsync(new TaskCancellationParameters(nameof(TaskPenddingContext), Dns.GetHostName(), taskExecutionInstance.Id));
            }

            await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);
        }

        return false;
    }

    public ValueTask EnsureInitAsync()
    {
        if (_cancelTokenSource == null)
        {
            _cancelTokenSource = new CancellationTokenSource();
            _cancelTokenSource.CancelAfter(
                TimeSpan.FromSeconds(Math.Max(60 * 10, TaskDefinition.PenddingLimitTimeSeconds)));
            CancellationToken = _cancelTokenSource.Token;
        }

        return ValueTask.CompletedTask;
    }

    public async Task<bool> WaitForRunningTasksAsync(IRepository<TaskExecutionInstanceModel> repository)
    {
        while (!CancellationToken.IsCancellationRequested)
        {
            var queryResult = await QueryTaskExecutionInstancesAsync(repository,
                new QueryTaskExecutionInstanceListParameters
                {
                    NodeIdList = [NodeSessionId.NodeId.Value],
                    TaskDefinitionIdList = [TaskDefinition.Id],
                    Status = TaskExecutionStatus.Running
                }, CancellationToken);
            if (!queryResult.HasValue) break;
            await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);
        }

        return true;
    }


    public async Task<ListQueryResult<TaskExecutionInstanceModel>> QueryTaskExecutionInstancesAsync(
        IRepository<TaskExecutionInstanceModel> repository,
        QueryTaskExecutionInstanceListParameters queryParameters,
        CancellationToken cancellationToken = default)
    {
        var queryResult = await repository.PaginationQueryAsync(new TaskExecutionInstanceSpecification(
                queryParameters.Keywords,
                queryParameters.Status,
                queryParameters.NodeIdList,
                queryParameters.TaskDefinitionIdList,
                queryParameters.TaskExecutionInstanceIdList,
                queryParameters.SortDescriptions),
            cancellationToken: cancellationToken);
        return queryResult;
    }

    public async ValueTask CancelAsync()
    {
        if (!_cancelTokenSource.IsCancellationRequested) await _cancelTokenSource.CancelAsync();
    }
}