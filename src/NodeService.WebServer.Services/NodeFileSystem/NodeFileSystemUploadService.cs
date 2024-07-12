using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.NodeFileSystem;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Entities;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Services.NodeFileSystem;

public partial class NodeFileSystemUploadService : BackgroundService
{
    readonly ILogger<NodeFileSystemUploadService> _logger;
    readonly IServiceProvider _serviceProvider;
    readonly ExceptionCounter _exceptionCounter;
    readonly WebServerCounter _webServerCounter;
    readonly BatchQueue<BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncRecordModel, NodeFileUploadContext>> _fileSyncOperationQueueBatchQueue;
    readonly BatchQueue<BatchQueueOperation<NodeFileSystemWatchEvent, bool>> _nodeFileSystemWatchEventQueue;
    readonly BatchQueue<BatchQueueOperation<NodeFileSystemSyncRecordServiceParameters, NodeFileSystemSyncRecordServiceResult>> _nodeFileSystemSyncRecordQueue;

    readonly ApplicationRepositoryFactory<FtpConfigModel> _ftpConfigRepoFactory;
    readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepoFactory;
    readonly ActionBlock<BatchProcessQueue> _batchProcessQueueActionBlock;
    readonly NodeBatchProcessQueueDictionary _batchProcessBatchQueueDict;
    readonly BatchQueue<NodeFileSyncRecordModel> _syncRecordAddOrUpdateActionBlock;

    public NodeFileSystemUploadService(
        ILogger<NodeFileSystemUploadService> logger,
        IServiceProvider serviceProvider,
        ExceptionCounter exceptionCounter,
        WebServerCounter webServerCounter,
        NodeBatchProcessQueueDictionary batchProcessQueueDictionary,
        BatchQueue<BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncRecordModel, NodeFileUploadContext>> fileSyncBatchQueue,
        BatchQueue<BatchQueueOperation<NodeFileSystemWatchEvent, bool>> nodeFileSystemWatchEventQueue,
        BatchQueue<BatchQueueOperation<NodeFileSystemSyncRecordServiceParameters, NodeFileSystemSyncRecordServiceResult>> nodeFileSystemSyncRecordQueue,
        ApplicationRepositoryFactory<FtpConfigModel> ftpConfigRepoFactory,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepoFactory)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _exceptionCounter = exceptionCounter;
        _webServerCounter = webServerCounter;
        _fileSyncOperationQueueBatchQueue = fileSyncBatchQueue;
        _nodeFileSystemWatchEventQueue = nodeFileSystemWatchEventQueue;
        _nodeFileSystemSyncRecordQueue = nodeFileSystemSyncRecordQueue;
        _ftpConfigRepoFactory = ftpConfigRepoFactory;
        _nodeInfoRepoFactory = nodeInfoRepoFactory;
        _syncRecordAddOrUpdateActionBlock = new BatchQueue<NodeFileSyncRecordModel>(1024, TimeSpan.FromSeconds(3));
        _batchProcessBatchQueueDict = batchProcessQueueDictionary;
        _batchProcessQueueActionBlock = new ActionBlock<BatchProcessQueue>(ProcessBatchProcessContextAsync, new ExecutionDataflowBlockOptions()
        {
            EnsureOrdered = true,
            MaxDegreeOfParallelism = Debugger.IsAttached ? 1 : 8,
        });
    }

    async ValueTask AddOrUpdateSyncRecordToBatchQueueAsync(NodeFileSyncRecordModel model)
    {
        await _syncRecordAddOrUpdateActionBlock.SendAsync(model);
    }

    async Task ProcessBatchProcessContextAsync(BatchProcessQueue batchProcessContext)
    {
        try
        {
            batchProcessContext.IsConnected = await batchProcessContext.ProcessContext.IdleAsync();
            var dbContextFactory = _serviceProvider.GetService<IDbContextFactory<InMemoryDbContext>>();
            using var dbContext = dbContextFactory.CreateDbContext();
            var idleCount = 0;
            while (idleCount < 600)
            {
                if (batchProcessContext.QueueCount > 0)
                {
                    await foreach (var uploadContext in ProcessBatchContextAsync(batchProcessContext))
                    {
                        idleCount = 0;
                        await SaveCacheAsync(dbContext, uploadContext);
                    }
                }

                batchProcessContext.IsConnected = await batchProcessContext.ProcessContext.IdleAsync();
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                idleCount++;
            }
            _batchProcessBatchQueueDict.TryRemove(batchProcessContext.QueueId, out _);
            await batchProcessContext.ProcessContext.DisposeAsync();
            _webServerCounter.NodeFileSyncServiceBatchProcessContextActiveCount.Value = _batchProcessBatchQueueDict.Count;
            _webServerCounter.NodeFileSyncServiceBatchProcessContextRemovedCount.Value++;
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }

    }

    async ValueTask SaveCacheAsync(
        InMemoryDbContext dbContext,
        NodeFileUploadContext uploadContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            switch (uploadContext.SyncRecord.Status)
            {
                case NodeFileSyncStatus.Unknown:
                case NodeFileSyncStatus.Pendding:
                case NodeFileSyncStatus.Queued:
                case NodeFileSyncStatus.Processing:
                    break;
                case NodeFileSyncStatus.Processed:
                case NodeFileSyncStatus.Skipped:
                    await SaveHittestResultCacheAsync(dbContext, uploadContext, cancellationToken);
                    break;
                case NodeFileSyncStatus.Canceled:

                    break;
                case NodeFileSyncStatus.Faulted:
     
                    break;
                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex, uploadContext.SyncRecord.NodeInfoId);
            _logger.LogInformation(ex.ToString());
        }
    }

    static async ValueTask SaveHittestResultCacheAsync(
        InMemoryDbContext dbContext,
        NodeFileUploadContext uploadContext,
        CancellationToken cancellationToken = default)
    {
        var cache = await dbContext.NodeFileHittestResultCacheDbSet.FindAsync(
            [
                uploadContext.SyncRecord.NodeInfoId,
                uploadContext.SyncRecord.FileInfo.FullName
            ], cancellationToken);
        if (cache == null)
        {
            cache = new NodeFileHittestResultCache()
            {
                NodeInfoId = uploadContext.SyncRecord.NodeInfoId,
                FullName = uploadContext.SyncRecord.FullName,
                DateTime = uploadContext.SyncRecord.FileInfo.LastWriteTime,
                Length = uploadContext.SyncRecord.FileInfo.Length,
                CreateDateTime = DateTime.UtcNow,
                ModifiedDateTime = DateTime.UtcNow
            };
            await dbContext.NodeFileHittestResultCacheDbSet.AddAsync(cache, cancellationToken);
        }
        else
        {
            if (uploadContext.IsStorageNotExists && uploadContext.SyncRecord.Status != NodeFileSyncStatus.Processed)
            {
                dbContext.NodeFileHittestResultCacheDbSet.Remove(cache);
            }
            else
            {
                cache.DateTime = uploadContext.SyncRecord.FileInfo.LastWriteTime;
                cache.Length = uploadContext.SyncRecord.FileInfo.Length;
                cache.ModifiedDateTime = DateTime.UtcNow;
                dbContext.NodeFileHittestResultCacheDbSet.Update(cache);
            }

        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    async IAsyncEnumerable<NodeFileUploadContext> ProcessBatchContextAsync(BatchProcessQueue batchProcessContext)
    {
        while (batchProcessContext.TryGetNextUploadContext(out NodeFileUploadContext? context))
        {
            if (context == null)
            {
                yield break;
            }
            context.SyncRecord.Status = NodeFileSyncStatus.Processing;
            await AddOrUpdateSyncRecordToBatchQueueAsync(context.SyncRecord);
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            await ProcessUploadContextAsync(batchProcessContext.ProcessContext, context, context.CancellationToken);

            batchProcessContext.ProcessedCount++;
            stopwatch.Stop();
            if (stopwatch.Elapsed > batchProcessContext.MaxProcessTime)
            {
                batchProcessContext.MaxProcessTime = stopwatch.Elapsed;
            }
            if (context.SyncRecord.Status == NodeFileSyncStatus.Processed)
            {
                batchProcessContext.TotalProcessedLength += context.SyncRecord.FileInfo.Length;
            }
            batchProcessContext.TotalProcessTime += stopwatch.Elapsed;
            batchProcessContext.AvgProcessTime = batchProcessContext.TotalProcessTime / batchProcessContext.ProcessedCount;
            await AddOrUpdateSyncRecordToBatchQueueAsync(context.SyncRecord);
            yield return context;
        }
        yield break;
    }

    NodeFileUploadGroupKey NodeFileGroup(BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncRecordModel, NodeFileUploadContext> op)
    {
        return new NodeFileUploadGroupKey()
        {
            ConfigurationId = op.Argument.ConfigurationId,
            ConfigurationProtocol = op.Argument.ConfigurationProtocol,
            NodeInfoId = op.Argument.NodeInfoId,
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
                    bool hasQueue = false;
                    do
                    {
                        for (int queueIndex = 0; queueIndex < 4; queueIndex++)
                        {
                            var queueId = $"{ftpConfig.Value}_{queueIndex}";
                            if (!_batchProcessBatchQueueDict.TryGetValue(
                                queueId,
                                out BatchProcessQueue? batchProcessQueue) || batchProcessQueue == null)
                            {
                                batchProcessQueue = await CreateBatchProcessQueueAsync(
                                    queueId,
                                    ftpConfig.Value!,
                                    cancellationToken);
                            }
                            if (batchProcessQueue == null)
                            {
                                continue;
                            }
                            if (batchProcessQueue.QueueCount > 100)
                            {
                                continue;
                            }
                            else
                            {
                                AddToBatchProcessContext(batchProcessQueue, opGroup);
                                await BatchAddOrUpdateAndSetResult(
                                           opGroup,
                                           NodeFileSyncStatus.Queued,
                                           0,
                                           null);
                                hasQueue = true;
                                break;
                            }
                        }
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    } while (!cancellationToken.IsCancellationRequested && !hasQueue);



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

    async ValueTask<BatchProcessQueue?> CreateBatchProcessQueueAsync(
        string queueId,
        FtpConfiguration ftpConfig,
        CancellationToken cancellationToken)
    {
        BatchProcessQueue? batchProcessQueue;
        var asyncFtpClient = CreateFtpClient(ftpConfig);
        var processContext = new FtpClientProcessContext(
            _serviceProvider.GetService<ILogger<FtpClientProcessContext>>(),
            _exceptionCounter,
            asyncFtpClient,
            _syncRecordAddOrUpdateActionBlock);
        batchProcessQueue = new BatchProcessQueue(queueId, $"{ftpConfig.Host}:{ftpConfig.Port}", processContext);
        _batchProcessBatchQueueDict.TryAdd(queueId, batchProcessQueue);
        _webServerCounter.NodeFileSyncServiceBatchProcessContextAddedCount.Value++;
        await _batchProcessQueueActionBlock.SendAsync(batchProcessQueue, cancellationToken);
        return batchProcessQueue;
    }

    void AddToBatchProcessContext(
        BatchProcessQueue batchContext,
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
  

                await processContext.ProcessAsync(
                    nodeFileUploadContext,
                    cancellationToken);

                //var nodeFilePath = NodeFileSystemHelper.GetNodeFilePath(
                //    nodeFileUploadContext.SyncRecord.NodeInfoId,
                //    nodeFileUploadContext.SyncRecord.FileInfo.FullName);

                //var nodeFilePathHash = NodeFileSystemHelper.GetNodeFilePathHash(nodeFilePath);
                //await AddOrUpdateFileSystemWatchRecordAsync(
                //    nodeFilePath,
                //    nodeFilePathHash,
                //    nodeFileUploadContext.SyncRecord.FileInfo,
                //    cancellationToken);
            }
            catch (Exception ex)
            {
                nodeFileUploadContext.TrySetException(ex);
                syncRecord.Status = NodeFileSyncStatus.Faulted;
                syncRecord.ErrorCode = ex.HResult;
                syncRecord.Message = ex.ToString();
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
            finally
            {
                await nodeFileUploadContext.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    async ValueTask AddOrUpdateFileSystemWatchRecordAsync(
    string nodeFilePath,
    string nodeFilePathHash,
    NodeFileInfo nodeFileInfo,
    CancellationToken cancellationToken = default)
    {
        return;
        var wapper = new NodeFileSystemWatchEvent()
        {
            NodeFilePath = nodeFilePath,
            NodeFilePathHash = nodeFilePathHash,
            ObjectInfo = nodeFileInfo
        };
        var op = new BatchQueueOperation<NodeFileSystemWatchEvent, bool>(
            wapper,
            BatchQueueOperationKind.AddOrUpdate);

        await _nodeFileSystemWatchEventQueue.SendAsync(
            op,
            cancellationToken);
    }

}