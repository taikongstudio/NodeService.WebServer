using Microsoft.EntityFrameworkCore.InMemory.Storage.Internal;
using NodeService.Infrastructure.NodeFileSystem;
using System.IO.Pipelines;

namespace NodeService.WebServer.Services.NodeFileSystem;

public class NodeFileUploadContext :  IAsyncDisposable
{
    public NodeFileUploadContext(
        NodeFileSyncRecordModel syncRecord,
        Pipe pipe)
    {
        SyncRecord = syncRecord;
        Pipe = pipe;
        _tcs = new TaskCompletionSource<NodeFileSyncStatus>();
        _cts = new CancellationTokenSource();
    }
    readonly CancellationTokenSource _cts;

    readonly TaskCompletionSource<NodeFileSyncStatus> _tcs;

    public NodeFileSyncRecordModel SyncRecord { get; private set; }

    public Pipe Pipe { get; private set; }

    public bool IsCancellationRequested { get; set; }

    public bool IsStorageNotExists { get; set; }

    public CancellationToken CancellationToken { get { return _cts.Token; } }

    public Task<NodeFileSyncStatus> WaitAsync(CancellationToken cancellationToken)
    {
        return _tcs.Task.WaitAsync(cancellationToken);
    }

    public bool TrySetResult(NodeFileSyncStatus status)
    {
        return _tcs.TrySetResult(status);
    }

    public bool TrySetException(Exception exception)
    {
        return _tcs.TrySetException(exception);
    }

    public bool TrySetCanceled()
    {
        return _tcs.TrySetCanceled();
    }

    public async ValueTask DisposeAsync()
    {
        if (!this._cts.IsCancellationRequested)
        {
            this._cts.Cancel();
        }
        this._cts.Dispose();
        await ValueTask.CompletedTask;
    }
}
