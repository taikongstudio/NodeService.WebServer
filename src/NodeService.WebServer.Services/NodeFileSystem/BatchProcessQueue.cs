namespace NodeService.WebServer.Services.NodeFileSystem;



public record class BatchProcessQueue
{
    ConcurrentQueue<NodeFileUploadContext> _uploadQueue;

    ConcurrentDictionary<NodeFileKey, NodeFileUploadContext> _uploadQueueFileDictionary;

    public BatchProcessQueue(string queueId, string queueName, ProcessContext processContext)
    {
        QueueId = queueId;
        QueueName = queueName;
        ProcessContext = processContext;
        _uploadQueue = new ConcurrentQueue<NodeFileUploadContext>();
        _uploadQueueFileDictionary = new ConcurrentDictionary<NodeFileKey, NodeFileUploadContext>();
        CreationDateTime = DateTime.UtcNow;
    }

    public  string QueueId { get; init; }

    public string QueueName { get; init; }

    public  ProcessContext ProcessContext { get; init; }

    public DateTime CreationDateTime { get; init; }

    public bool IsConnected { get; set; }

    public int ProcessedCount { get;  set; }

    public long MaxFileLengthInQueue { get; private set; }

    public long MaxFileLength { get; private set; }

    public long TotalProcessedLength { get; set; }

    public TimeSpan TotalProcessTime { get; set; }

    public TimeSpan MaxProcessTime { get; set; }

    public TimeSpan AvgProcessTime { get; set; }

    public int QueueCount { get { return _uploadQueue.Count; } }

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

    public long GetCurrentTotalLength()
    {
        return this._uploadQueueFileDictionary.Values.Sum(static x => x.SyncRecord.FileInfo.Length);
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
            if (nodeFileUploadContext.SyncRecord.FileInfo.Length > MaxFileLength)
            {
                MaxFileLength = nodeFileUploadContext.SyncRecord.FileInfo.Length;
            }
            MaxFileLengthInQueue = _uploadQueueFileDictionary.IsEmpty ? 0 : _uploadQueueFileDictionary.Max(static x => x.Value.SyncRecord.FileInfo.Length);
            return true;
        }
        return false;
    }
}
