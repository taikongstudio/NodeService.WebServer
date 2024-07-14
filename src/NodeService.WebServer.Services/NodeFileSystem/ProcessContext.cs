namespace NodeService.WebServer.Services.NodeFileSystem;

public abstract class ProcessContext : IAsyncDisposable
{
    public abstract ValueTask DisposeAsync();

    public abstract ValueTask ProcessAsync(NodeFileSyncContext nodeFileUploadContext, CancellationToken cancellationToken = default);
}
