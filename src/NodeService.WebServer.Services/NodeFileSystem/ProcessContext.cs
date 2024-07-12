namespace NodeService.WebServer.Services.NodeFileSystem;

public abstract class ProcessContext : IAsyncDisposable
{
    public virtual ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public abstract ValueTask<bool> IdleAsync(CancellationToken cancellationToken = default);
    public abstract ValueTask ProcessAsync(NodeFileUploadContext nodeFileUploadContext, CancellationToken cancellationToken = default);
}
