using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using System.Net;

namespace NodeService.WebServer.Services.Tasks;

public class TaskPenddingContext : IAsyncDisposable
{
    private CancellationTokenSource _cancelTokenSource;

    public TaskPenddingContext(string id, TaskActivationRecordModel taskActivationRecord, TaskDefinition taskDefinition)
    {
        Id = id;
        TaskDefinition = taskDefinition;
        TaskActivationRecord = taskActivationRecord;
    }

    public string Id { get; }

    public INodeSessionService NodeSessionService { get; init; }

    public required NodeSessionId NodeSessionId { get; init; }

    public TaskActivationRecordModel  TaskActivationRecord { get; private set; }

    public TaskDefinition TaskDefinition { get; private set; }

    public required TaskExecutionEventRequest TriggerEvent { get; init; }

    public CancellationToken CancellationToken { get; private set; }

    public required FireTaskParameters FireParameters { get; init; }

    public ValueTask DisposeAsync()
    {
        _cancelTokenSource.Dispose();
        return ValueTask.CompletedTask;
    }



    public void EnsureInit()
    {
        if (_cancelTokenSource == null)
        {
            _cancelTokenSource = new CancellationTokenSource();
            var penddingTimeLimitSeconds = TimeSpan.FromSeconds(TaskActivationRecord.GetTaskDefinition().PenddingLimitTimeSeconds);
            _cancelTokenSource.CancelAfter(penddingTimeLimitSeconds);
            CancellationToken = _cancelTokenSource.Token;
        }
    }



    public async ValueTask CancelAsync()
    {
        if (!_cancelTokenSource.IsCancellationRequested) await _cancelTokenSource.CancelAsync();
    }
}