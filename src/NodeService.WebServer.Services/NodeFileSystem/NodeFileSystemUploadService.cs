using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.NodeFileSystem;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;
using System.Collections.Immutable;
using System.Threading.Tasks.Dataflow;

namespace NodeService.WebServer.Services.NodeFileSystem;

public record struct NodeFileUploadGroupKey
{
    public string NodeInfoId { get; init; }
    public NodeFileSyncConfigurationProtocol ConfigurationProtocol { get; init; }
    public string ConfigurationId { get; init; }
    public long LengthBase { get; init; }
}

public record struct NodeFileKey
{
    public NodeFileKey(string nodeInfoId, string fullName)
    {
        NodeInfoId = nodeInfoId;
        FullName = fullName;
    }

    public string NodeInfoId { get; private set; }

    public string FullName { get; private set; }
}

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

public abstract class ProcessContext : IAsyncDisposable
{
    public virtual ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
    public abstract ValueTask ProcessAsync(NodeFileUploadContext nodeFileUploadContext, CancellationToken cancellationToken = default);
}

public class FtpClientProcessContext : ProcessContext
{

    readonly AsyncFtpClient _asyncFtpClient;
    readonly BatchQueue<NodeFileSyncRecordModel> _actionBlock;

    public FtpClientProcessContext(AsyncFtpClient asyncFtpClient, BatchQueue<NodeFileSyncRecordModel> actionBlock)
    {
        _asyncFtpClient = asyncFtpClient;
        _actionBlock = actionBlock;
    }

    async ValueTask DisposeFtpClientAsync()
    {
        if (this._asyncFtpClient == null)
        {
            return;
        }
        await _asyncFtpClient.Disconnect();
        _asyncFtpClient.Dispose();
    }

    public override async ValueTask DisposeAsync()
    {
        await DisposeFtpClientAsync();
    }

    public override async ValueTask ProcessAsync(NodeFileUploadContext nodeFileUploadContext, CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(nodeFileUploadContext);
            await _asyncFtpClient.AutoConnect(cancellationToken);
            nodeFileUploadContext.SyncRecord.UtcBeginTime = DateTime.UtcNow;
            nodeFileUploadContext.SyncRecord.Status = NodeFileSyncStatus.Processing;
            var lambdaProgress = new LambdaProgress<FtpProgress>((ftpProgress) =>
            {
                nodeFileUploadContext.SyncRecord.Progress = ftpProgress.Progress;
                if (ftpProgress.Progress < 100)
                {
                    nodeFileUploadContext.SyncRecord.TransferSpeed = ftpProgress.TransferSpeed;
                }
                nodeFileUploadContext.SyncRecord.EstimatedTimeSpan = ftpProgress.ETA;
                nodeFileUploadContext.SyncRecord.TransferredBytes = ftpProgress.TransferredBytes;
                _actionBlock.Post(nodeFileUploadContext.SyncRecord);
            });
            var ftpStatus = await this._asyncFtpClient.UploadStream(
                nodeFileUploadContext.Stream,
                nodeFileUploadContext.SyncRecord.StoragePath,
                FtpRemoteExists.Overwrite,
                true,
                lambdaProgress,
                cancellationToken);
           await _asyncFtpClient.SetModifiedTime(nodeFileUploadContext.SyncRecord.StoragePath,
               nodeFileUploadContext.FileInfo.LastWriteTime,
               cancellationToken);
            nodeFileUploadContext.SyncRecord.Status = NodeFileSyncStatus.Processed;
        }
        catch (Exception ex)
        {
            nodeFileUploadContext.SyncRecord.Status = NodeFileSyncStatus.Faulted;
            nodeFileUploadContext.SyncRecord.ErrorCode = ex.HResult;
            nodeFileUploadContext.SyncRecord.Message = ex.Message;
        }
        finally
        {
            nodeFileUploadContext.SyncRecord.UtcEndTime = DateTime.UtcNow;
        }
    }
}

public partial class NodeFileSystemUploadService : BackgroundService
{
    readonly ILogger<NodeFileSystemUploadService> _logger;
    readonly ExceptionCounter _exceptionCounter;
    readonly WebServerCounter _webServerCounter;
    readonly BatchQueue<BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncRecordModel, NodeFileUploadContext>> _fileSyncOperationQueueBatchQueue;
    readonly BatchQueue<BatchQueueOperation<NodeFileSystemInfoEvent, bool>> _nodeFileSystemEventQueue;
    readonly BatchQueue<BatchQueueOperation<NodeFileSystemSyncRecordServiceParameters, NodeFileSystemSyncRecordServiceResult>> _nodeFileSystemSyncRecordQueue;

    readonly ApplicationRepositoryFactory<FtpConfigModel> _ftpConfigRepoFactory;
    readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepoFactory;
    readonly ActionBlock<BatchProcessContext> _nodeFtpUploadActionBlock;
    readonly ConcurrentDictionary<string, BatchProcessContext> _inprogressBatchContextDict;
    readonly BatchQueue<NodeFileSyncRecordModel> _syncRecordAddOrUpdateActionBlock;

    public NodeFileSystemUploadService(
        ILogger<NodeFileSystemUploadService> logger,
        ExceptionCounter exceptionCounter,
        WebServerCounter webServerCounter,
        BatchQueue<BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncRecordModel, NodeFileUploadContext>> fileSyncBatchQueue,
        BatchQueue<BatchQueueOperation<NodeFileSystemInfoEvent, bool>> nodeFileSystemEventQueue,
        BatchQueue<BatchQueueOperation<NodeFileSystemSyncRecordServiceParameters, NodeFileSystemSyncRecordServiceResult>> nodeFileSystemSyncRecordQueue,
        ApplicationRepositoryFactory<FtpConfigModel> ftpConfigRepoFactory,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepoFactory)
    {
        _logger = logger;
        _exceptionCounter = exceptionCounter;
        _webServerCounter = webServerCounter;
        _fileSyncOperationQueueBatchQueue = fileSyncBatchQueue;
        _nodeFileSystemEventQueue = nodeFileSystemEventQueue;
        _nodeFileSystemSyncRecordQueue = nodeFileSystemSyncRecordQueue;
        _ftpConfigRepoFactory = ftpConfigRepoFactory;
        _nodeInfoRepoFactory = nodeInfoRepoFactory;
        _syncRecordAddOrUpdateActionBlock = new BatchQueue<NodeFileSyncRecordModel>(1024, TimeSpan.FromSeconds(3));
        _inprogressBatchContextDict = new ConcurrentDictionary<string, BatchProcessContext>();
        _nodeFtpUploadActionBlock = new ActionBlock<BatchProcessContext>(ProcessBatchProcessContextAsync, new ExecutionDataflowBlockOptions()
        {
            EnsureOrdered = true,
            MaxDegreeOfParallelism = Debugger.IsAttached ? 1 : 8,
        });
    }

    async ValueTask AddOrUpdateSyncRecordToBatchQueueAsync(NodeFileSyncRecordModel model)
    {
        await _syncRecordAddOrUpdateActionBlock.SendAsync(model);
    }

    async Task ProcessBatchProcessContextAsync(BatchProcessContext batchContext)
    {
        int count = 0;
        while (count < 600)
        {
            if (await ProcessBatchContextAsync(batchContext))
            {
                count = 0;
                continue;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            count++;
        }
        _inprogressBatchContextDict.TryRemove(batchContext.ContextId, out _);
        await batchContext.ProcessContext.DisposeAsync();
        _webServerCounter.NodeFileSyncServiceBatchProcessContextActiveCount.Value = _inprogressBatchContextDict.Count;
        _webServerCounter.NodeFileSyncServiceBatchProcessContextRemovedCount.Value++;
    }

    async ValueTask<bool> ProcessBatchContextAsync(BatchProcessContext batchContext)
    {
        var count = 0;
        while (batchContext.TryGetNextUploadContext(out NodeFileUploadContext? nodeFileUploadContext))
        {
            if (nodeFileUploadContext == null)
            {
                break;
            }
            nodeFileUploadContext.SyncRecord.Status = NodeFileSyncStatus.Processing;
            await AddOrUpdateSyncRecordToBatchQueueAsync(nodeFileUploadContext.SyncRecord);
            await ProcessUploadContextAsync(batchContext.ProcessContext, nodeFileUploadContext);
            await AddOrUpdateSyncRecordToBatchQueueAsync(nodeFileUploadContext.SyncRecord);
            count++;
        }
        return count > 0;
    }

    NodeFileUploadGroupKey NodeFileGroup(BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncRecordModel, NodeFileUploadContext> op)
    {
        return new NodeFileUploadGroupKey()
        {
            ConfigurationId = op.Argument.ConfigurationId,
            ConfigurationProtocol = op.Argument.ConfigurationProtocol,
            NodeInfoId = op.Argument.NodeInfoId,
            LengthBase = op.Argument.Stream.Length / (1024 * 1024 * 100)
        };
    }



    protected override async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(
            ProcessNodeSyncRecordQueueActionBlockAsync(cancellationToken),
            ConsumeNodeFileUploadOperationAsync(cancellationToken));
    }

    private async Task ProcessNodeSyncRecordQueueActionBlockAsync(CancellationToken cancellationToken = default)
    {
        await foreach (var array in _syncRecordAddOrUpdateActionBlock.ReceiveAllAsync(cancellationToken))
        {
            await BatchAddOrUpdateSyncRecordsAsync(array, cancellationToken);
        }
    }

    async Task ConsumeNodeFileUploadOperationAsync(CancellationToken cancellationToken = default)
    {
        await foreach (var array in _fileSyncOperationQueueBatchQueue.ReceiveAllAsync(cancellationToken))
        {
            if (array == null || array.Length == 0)
            {
                continue;
            }
            var syncRecords = array.Select(static x => x.Context.SyncRecord).ToArray();
            try
            {
                foreach (var syncRecord in syncRecords)
                {
                    await AddOrUpdateSyncRecordToBatchQueueAsync(syncRecord);
                }
                var nodeInfoIdList = syncRecords.Select(x => x.NodeInfoId).Distinct();
                var nodeInfoList = await FindNodeInfoListAsync(nodeInfoIdList, cancellationToken);
                if (nodeInfoList == null || nodeInfoList.Count == 0)
                {
                    await BatchAddOrUpdateAndSetResult(
                                 array,
                                  NodeFileSyncStatus.Faulted,
                                 -1,
                                 "node info not found");
                }
                else
                {
                    foreach (var opGroup in array.GroupBy(NodeFileGroup))
                    {
                        await ProcessGroupAsync(nodeInfoList, opGroup, cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                await BatchAddOrUpdateAndSetResult(array, NodeFileSyncStatus.Faulted, ex.HResult, ex.ToString());
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
            finally
            {

            }
        }
    }

    private async Task ProcessGroupAsync(List<NodeInfoModel>? nodeInfoList, IGrouping<NodeFileUploadGroupKey, BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncRecordModel, NodeFileUploadContext>> opGroup, CancellationToken cancellationToken)
    {
        try
        {
            var key = opGroup.Key;
            if (key.NodeInfoId == null)
            {
                await BatchAddOrUpdateAndSetResult(
                         opGroup,
                         NodeFileSyncStatus.Faulted,
                         -1,
                         "invalid node info id");
            }
            NodeInfoModel? nodeInfoModel = null;
            foreach (var item in nodeInfoList)
            {
                if (key.NodeInfoId == item.Id)
                {
                    nodeInfoModel = item;
                    break;
                }
            }
            if (nodeInfoModel == null)
            {
                await BatchAddOrUpdateAndSetResult(
                      opGroup,
                      NodeFileSyncStatus.Faulted,
                      -1,
                      "node info not found");
                return;
            }
            switch (key.ConfigurationProtocol)
            {
                case NodeFileSyncConfigurationProtocol.Unknown:
                    await BatchAddOrUpdateAndSetResult(
                          opGroup,
                          NodeFileSyncStatus.Faulted,
                          -1,
                          "unknown protocol");
                    break;
                case NodeFileSyncConfigurationProtocol.Ftp:
                    var ftpConfig = await FindFtpConfigurationAsync(key.ConfigurationId, cancellationToken);

                    if (ftpConfig == null)
                    {
                        await BatchAddOrUpdateAndSetResult(
                                 opGroup,
                                 NodeFileSyncStatus.Faulted,
                                 -1,
                                 "invalid ftp configuration id");
                        return;
                    }


                    var contextId = ftpConfig.Value.ToString();

                    await BatchAddOrUpdateAndSetResult(
                                                           opGroup,
                                                           NodeFileSyncStatus.Queued,
                                                           0,
                                                           null);

                    if (!_inprogressBatchContextDict.TryGetValue(contextId, out BatchProcessContext? batchProcessContext) || batchProcessContext == null)
                    {
                        var asyncFtpClient = CreateFtpClient(ftpConfig);
                        var processContext = new FtpClientProcessContext(asyncFtpClient, _syncRecordAddOrUpdateActionBlock);
                        batchProcessContext = new BatchProcessContext(contextId, processContext);
                        AddToBatchProcessContext(batchProcessContext, opGroup);
                        _inprogressBatchContextDict.TryAdd(contextId, batchProcessContext);
                        _webServerCounter.NodeFileSyncServiceBatchProcessContextAddedCount.Value++;
                        await _nodeFtpUploadActionBlock.SendAsync(batchProcessContext, cancellationToken);
                    }
                    else
                    {
                        AddToBatchProcessContext(batchProcessContext, opGroup);
                    }
                    break;
                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            await BatchAddOrUpdateAndSetResult(
                  opGroup,
                  NodeFileSyncStatus.Faulted,
                  ex.HResult,
                  ex.Message);
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    void AddToBatchProcessContext(
        BatchProcessContext batchContext,
        IEnumerable<BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncRecordModel, NodeFileUploadContext>> ops

   )
    {
        foreach (var op in ops)
        {
            if (op.Context == null)
            {
                continue;
            }
            batchContext.AddNodeFileUploadContext(op.Context);
        }
    }

    async ValueTask BatchAddOrUpdateSyncRecordsAsync(
        IEnumerable<NodeFileSyncRecordModel> syncRecords,
        CancellationToken cancellationToken = default)
    {
        var parameters = new NodeFileSystemAddOrUpdateSyncRecordParameters(syncRecords);
        var argument = new NodeFileSystemSyncRecordServiceParameters(parameters);
        var op = new BatchQueueOperation<NodeFileSystemSyncRecordServiceParameters, NodeFileSystemSyncRecordServiceResult>(
            argument, BatchQueueOperationKind.AddOrUpdate);
        await _nodeFileSystemSyncRecordQueue.SendAsync(op, cancellationToken);
        var result = await op.WaitAsync(cancellationToken);
    }

    async Task<List<NodeInfoModel>?> FindNodeInfoListAsync(IEnumerable<string> nodeInfoIdList, CancellationToken cancellationToken = default)
    {
        using var nodeInfoRepo = _nodeInfoRepoFactory.CreateRepository();
        var nodeInfoList = await nodeInfoRepo.ListAsync(new ListSpecification<NodeInfoModel>(DataFilterCollection<string>.Includes(nodeInfoIdList)), cancellationToken);
        return nodeInfoList;
    }

    async ValueTask BatchAddOrUpdateAndSetResult(
        IEnumerable<BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncRecordModel, NodeFileUploadContext>> opGroup,
        NodeFileSyncStatus status,
        int errorCode,
        string message)
    {
        foreach (var op in opGroup)
        {
            var syncRecord = op.Context.SyncRecord;
            syncRecord.ErrorCode = errorCode;
            syncRecord.Message = message;
            syncRecord.Status = status;
            await AddOrUpdateSyncRecordToBatchQueueAsync(op.Context.SyncRecord);
        }

        foreach (var op in opGroup)
        {
            var syncRecord = op.Context.SyncRecord;
            op.SetResult(syncRecord);
        }
    }

    async ValueTask ProcessUploadContextAsync(
        ProcessContext processContext,
        NodeFileUploadContext nodeFileUploadContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var syncRecord = nodeFileUploadContext.SyncRecord;
            try
            {
                if (nodeFileUploadContext.IsCancellationRequested)
                {
                    syncRecord.Status = NodeFileSyncStatus.Canceled;
                    return;
                }
                await processContext.ProcessAsync(
                    nodeFileUploadContext,
                    cancellationToken);

                var nodeFilePath = NodeFileSystemHelper.GetNodeFilePath(
                    nodeFileUploadContext.SyncRecord.NodeInfoId,
                    nodeFileUploadContext.SyncRecord.FileInfo.FullName);

                var nodeFilePathHash = NodeFileSystemHelper.GetNodeFilePathHash(nodeFilePath);
                await AddOrUpdateFileSystemWatchRecordAsync(
                    nodeFilePath,
                    nodeFilePathHash,
                    nodeFileUploadContext.SyncRecord.FileInfo,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                syncRecord.Status = NodeFileSyncStatus.Faulted;
                syncRecord.ErrorCode = ex.HResult;
                syncRecord.Message = ex.Message;
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
            finally
            {
                nodeFileUploadContext.Dispose();
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    async Task AddOrUpdateFileSystemWatchRecordAsync(
    string nodeFilePath,
    string nodeFilePathHash,
    NodeFileInfo  nodeFileInfo,
    CancellationToken cancellationToken = default)
    {
        var wapper = new NodeFileSystemInfoEvent()
        {
            NodeFilePath = nodeFilePath,
            NodeFilePathHash = nodeFilePathHash,
            ObjectInfo = nodeFileInfo
        };
        var op = new BatchQueueOperation<NodeFileSystemInfoEvent, bool>(
            wapper,
            BatchQueueOperationKind.AddOrUpdate);

        await _nodeFileSystemEventQueue.SendAsync(
            op,
            cancellationToken);
    }

}