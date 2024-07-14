using Microsoft.EntityFrameworkCore.InMemory.Storage.Internal;
using NodeService.Infrastructure.NodeFileSystem;
using System.IO.Pipelines;

namespace NodeService.WebServer.Services.NodeFileSystem;

public class NodeFileSyncContext
{
    readonly CancellationTokenSource _cts;

    readonly TaskCompletionSource<NodeFileSyncStatus> _tcs;


    public NodeFileSyncContext(
        NodeFileSyncRequest request,
        NodeFileSyncRecordModel record,
        Stream stream)
    {
        Request = request;
        Record = record;
        Stream = stream;
        _tcs = new TaskCompletionSource<NodeFileSyncStatus>();
        _cts = new CancellationTokenSource();
    }


    public NodeFileSyncRecordModel Record { get; private set; }

    public Stream Stream { get; private set; }

    public NodeFileSyncRequest Request { get; private set; }

    public bool IsStorageNotExists { get; set; }

    public CancellationToken CancellationToken { get { return _cts.Token; } }

    public Task<NodeFileSyncStatus> WaitAsync(CancellationToken cancellationToken)
    {
        return _tcs.Task.WaitAsync(cancellationToken);
    }

    public async ValueTask CancelAsync()
    {
        await this._cts.CancelAsync();
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
}
