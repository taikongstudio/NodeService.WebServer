using NodeService.Infrastructure.NodeFileSystem;
using NodeService.WebServer.Data.Entities;
using NodeService.WebServer.Data;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Services.NodeFileSystem;



public  class NodeFileSyncQueue
{
    readonly ConcurrentDictionary<NodeFileKey, NodeFileSyncContext> _dict;
    readonly ActionBlock<NodeFileSyncContext> _actionBlock;
    readonly ProgressBlock<NodeFileSyncRecordModel> _progressBlock;
    readonly WebServerCounter _webServerCounter;


    public  string Id { get; private set; }

    public string Name { get; private set; }

    public  ProcessContext ProcessContext { get; private set; }

    readonly ILogger<NodeFileSyncQueue> _logger;
    readonly IDbContextFactory<InMemoryDbContext> _dbContextFactory;
    readonly ExceptionCounter _exceptionCounter;

    public DateTime CreationDateTime { get; private set; }

    public bool IsConnected { get; set; }

    public int ProcessedCount { get;  set; }

    public long MaxFileLengthInQueue { get; private set; }

    public long MaxFileLength { get; private set; }

    public long TotalProcessedLength { get; set; }

    public TimeSpan TotalProcessTime { get; set; }

    public TimeSpan MaxProcessTime { get; set; }

    public TimeSpan AvgProcessTime { get; set; }

    public int QueueCount { get { return _dict.Count; } }

    public NodeFileSyncQueue(
    string id,
    string name,
    ILogger<NodeFileSyncQueue> logger,
    ProcessContext processContext,
    WebServerCounter webServerCounter,
    ExceptionCounter exceptionCounter,
    IDbContextFactory<InMemoryDbContext> dbContextFactory,
    ProgressBlock<NodeFileSyncRecordModel> progressBlock)
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _exceptionCounter = exceptionCounter;
        _dict = new ConcurrentDictionary<NodeFileKey, NodeFileSyncContext>();
        CreationDateTime = DateTime.UtcNow;
        _actionBlock = new ActionBlock<NodeFileSyncContext>(ProcessSyncContextAsync, new ExecutionDataflowBlockOptions()
        {
            EnsureOrdered = true,
            MaxDegreeOfParallelism = 1
        });
        _progressBlock = progressBlock;
        Id = id;
        Name = name;
        ProcessContext = processContext;
    }

    void AddOrUpdate(
    NodeFileSyncRecordModel record,
    NodeFileSyncStatus status,
    int errorCode,
    string message)
    {
        record.ErrorCode = errorCode;
        record.Message = message;
        record.Status = status;
        AddOrUpdate(record);
    }

    private void AddOrUpdate(NodeFileSyncRecordModel record)
    {
        _progressBlock.Report(record);
    }

    public async Task AddSyncContextAsync(NodeFileSyncContext context)
    {
        var key = new NodeFileKey(
            context.Record.NodeInfoId,
            context.Record.FullName);
        if (_dict.TryRemove(key, out var oldContext))
        {
            oldContext.TrySetCanceled();
        }
        _dict.TryAdd(key, context);
        await _actionBlock.SendAsync(context);
    }

    public long GetCurrentTotalLength()
    {
        return this._dict.Values.Sum(static x => x.Record.FileInfo.Length);
    }

    private async Task ProcessSyncContextAsync(NodeFileSyncContext context)
    {
        AddOrUpdate(context.Record, NodeFileSyncStatus.Processing, 0, null);
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        await ProcessAsyncContextAsync(ProcessContext,
                                        context,
                                        context.CancellationToken);
        if (context.Record.Status == NodeFileSyncStatus.Processed)
        {
            TotalProcessedLength += context.Record.FileInfo.Length;
        }

        ProcessedCount++;
        stopwatch.Stop();
        if (stopwatch.Elapsed > MaxProcessTime)
        {
            MaxProcessTime = stopwatch.Elapsed;
        }
        TotalProcessTime += stopwatch.Elapsed;
        AvgProcessTime = TotalProcessTime / ProcessedCount;
        AddOrUpdate(context.Record);

    }

    async ValueTask ProcessAsyncContextAsync(
    ProcessContext processContext,
    NodeFileSyncContext syncContext,
    CancellationToken cancellationToken = default)
    {
        try
        {
            var syncRecord = syncContext.Record;
            try
            {


                await processContext.ProcessAsync(
                    syncContext,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                syncContext.TrySetException(ex);
                syncRecord.Status = NodeFileSyncStatus.Faulted;
                syncRecord.ErrorCode = ex.HResult;
                syncRecord.Message = ex.ToString();
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
            finally
            {

            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }


}
