using NodeService.Infrastructure.NodeFileSystem;

namespace NodeService.WebServer.Services.NodeFileSystem;

public class NodeFileUploadContext :  IDisposable
{
    public NodeFileUploadContext(
        NodeFileInfo fileInfo,
        NodeFileSyncRecordModel syncRecord,
        Stream stream)
    {
        FileInfo = fileInfo;
        SyncRecord = syncRecord;
        Stream = stream;
    }

    public NodeFileInfo FileInfo { get; private set; }

    public NodeFileSyncRecordModel SyncRecord { get; private set; }

    public Stream Stream { get; private set; }

    public bool IsCancellationRequested { get; set; }

    public void Dispose()
    {
        this.Stream.Dispose();
        if (this.Stream is FileStream fileStream)
        {
            File.Delete(fileStream.Name);
        }
    }

}
