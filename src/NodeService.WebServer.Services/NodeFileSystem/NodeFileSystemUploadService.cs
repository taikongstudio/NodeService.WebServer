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

public record class BatchProcessContext
{
    public required string ContextId { get; init; }
    public required ProcessContext ProcessContext { get; init; }
    public required ConcurrentQueue<NodeFileUploadContext> UploadQueue { get; init; }
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
    readonly ActionBlock<NodeFileSyncRecordModel> _actionBlock;

    public FtpClientProcessContext(AsyncFtpClient asyncFtpClient, ActionBlock<NodeFileSyncRecordModel> actionBlock)
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
                nodeFileUploadContext.SyncRecord.TransferSpeed = ftpProgress.TransferSpeed;
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
    readonly ActionBlock<NodeFileSyncRecordModel> _syncRecordAddOrUpdateActionBlock;

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
        _syncRecordAddOrUpdateActionBlock = new ActionBlock<NodeFileSyncRecordModel>(AddOrUpdateSyncRecordAsync, new ExecutionDataflowBlockOptions()
        {
            EnsureOrdered = true,
            MaxDegreeOfParallelism = 1,
        });
        _inprogressBatchContextDict = new ConcurrentDictionary<string, BatchProcessContext>();
        _nodeFtpUploadActionBlock = new ActionBlock<BatchProcessContext>(ProcessNodeFtpBatchContextAsync, new ExecutionDataflowBlockOptions()
        {
            EnsureOrdered = true,
            MaxDegreeOfParallelism = Debugger.IsAttached ? 1 : 8,
        });
    }

    async Task AddOrUpdateSyncRecordAsync(NodeFileSyncRecordModel model)
    {
        await BatchAddOrUpdateSyncRecordsAsync([model]);
    }

    async Task ProcessNodeFtpBatchContextAsync(BatchProcessContext batchContext)
    {
        int count = 0;
        while (count < 60)
        {
            await ProcessBatchContext(batchContext);
            await Task.Delay(TimeSpan.FromSeconds(1));
            count++;
        }
        _inprogressBatchContextDict.TryRemove(batchContext.ContextId, out _);
        await batchContext.ProcessContext.DisposeAsync();
        _webServerCounter.NodeFileSyncServiceBatchProcessContextActiveCount.Value = _inprogressBatchContextDict.Count;
        _webServerCounter.NodeFileSyncServiceBatchProcessContextRemovedCount.Value++;
    }

    async Task ProcessBatchContext(BatchProcessContext batchContext)
    {
        while (!batchContext.UploadQueue.IsEmpty)
        {
            if (!batchContext.UploadQueue.TryDequeue(out NodeFileUploadContext? nodeFileUploadContext))
            {
                continue;
            }
            nodeFileUploadContext.SyncRecord.Status = NodeFileSyncStatus.Processing;
            await BatchAddOrUpdateSyncRecordsAsync([nodeFileUploadContext.SyncRecord]);
            await ProcessUploadContextAsync(batchContext.ProcessContext, nodeFileUploadContext);
            await BatchAddOrUpdateSyncRecordsAsync([nodeFileUploadContext.SyncRecord]);
        }


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
        await ConsumeNodeFileUploadOperationAsync(cancellationToken);
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

                await BatchAddOrUpdateSyncRecordsAsync(syncRecords, cancellationToken);
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
                await BatchAddOrUpdateSyncRecordsAsync(syncRecords, cancellationToken);
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

    
                    var contextId = ftpConfig.ToString();
                    if (!_inprogressBatchContextDict.TryGetValue(contextId, out BatchProcessContext? batchContext) || batchContext == null)
                    {
                        var asyncFtpClient = CreateFtpClient(ftpConfig);
                        batchContext = new BatchProcessContext()
                        {
                            ContextId = contextId,
                            ProcessContext = new FtpClientProcessContext(asyncFtpClient, _syncRecordAddOrUpdateActionBlock),
                            UploadQueue = new ConcurrentQueue<NodeFileUploadContext>(opGroup.Select(x => x.Context))
                        };
                        _inprogressBatchContextDict.TryAdd(contextId, batchContext);
                        _webServerCounter.NodeFileSyncServiceBatchProcessContextAddedCount.Value++;
                        await _nodeFtpUploadActionBlock.SendAsync(batchContext, cancellationToken);
                    }
                    else
                    {
                        foreach (var context in opGroup.Select(static x => x.Context))
                        {
                            if (context == null)
                            {
                                continue;
                            }
                            batchContext.UploadQueue.Enqueue(context);
                        }
                    }

                    await BatchAddOrUpdateAndSetResult(
                       opGroup,
                       NodeFileSyncStatus.Queued,
                       0,
                       null);
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
        }

        await BatchAddOrUpdateSyncRecordsAsync(opGroup.Select(x => x.Context.SyncRecord));

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
                if (nodeFileUploadContext == null)
                {
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

    async ValueTask<FtpConfigModel?> FindFtpConfigurationAsync(string configurationId, CancellationToken cancellationToken)
    {
        using var ftpConfigRepo = _ftpConfigRepoFactory.CreateRepository();
        var ftpConfig = await ftpConfigRepo.GetByIdAsync(
            configurationId,
            cancellationToken);
        return ftpConfig;
    }
}