namespace NodeService.WebServer.Services.NodeFileSystem;

public record class BatchProcessContext
{
    public BatchProcessContext(string contextId, ProcessContext processContext)
    {
        ContextId = contextId;
        ProcessContext = processContext;
        _uploadQueue = new ConcurrentQueue<NodeFileUploadContext>();
        _uploadQueueFileDictionary = new ConcurrentDictionary<NodeFileKey, NodeFileUploadContext>();
    }

    public  string ContextId { get; init; }
    public  ProcessContext ProcessContext { get; init; }
    ConcurrentQueue<NodeFileUploadContext> _uploadQueue;

    ConcurrentDictionary<NodeFileKey, NodeFileUploadContext> _uploadQueueFileDictionary;

    public void AddNodeFileUploadContext(NodeFileUploadContext nodeFileUploadContext)
    {
        _uploadQueue.Enqueue(nodeFileUploadContext);
        var key = new NodeFileKey(
            nodeFileUploadContext.SyncRecord.NodeInfoId,
            nodeFileUploadContext.SyncRecord.FullName);
        if (_uploadQueueFileDictionary.TryRemove(key, out var oldNodeFileUploadContext))
        {
            oldNodeFileUploadContext.IsCancellationRequested = true;
        }
        _uploadQueueFileDictionary.TryAdd(key, nodeFileUploadContext);
    }

    public bool TryGetNextUploadContext(out NodeFileUploadContext? nodeFileUploadContext)
    {
        nodeFileUploadContext = default;
        if (_uploadQueue.IsEmpty)
        {
            return false;
        }
        if (_uploadQueue.TryDequeue(out nodeFileUploadContext))
        {
            var key = new NodeFileKey(
            nodeFileUploadContext.SyncRecord.NodeInfoId,
            nodeFileUploadContext.SyncRecord.FullName);
            _uploadQueueFileDictionary.TryRemove(key, out _);
            return true;
        }
        return false;
    }
}
