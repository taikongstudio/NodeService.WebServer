using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.NodeFileSystem;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;
using System.Text.Json.Nodes;

namespace NodeService.WebServer.Services.NodeFileSystem;

public record struct NodeFileUploadGroupKey
{
    public string NodeInfoId { get; init; }
    public NodeFileSyncConfigurationProtocol ConfigurationProtocol { get; init; }
    public string ConfigurationId { get; init; }
    public long LengthBase { get; init; }
}

public class NodeFileUploadContext : IProgress<FtpProgress>, IDisposable
{
    public NodeFileInfo FileInfo { get; init; }

    public NodeFileSyncRecord SyncRecord { get; init; }

    public Stream Stream { get; init; }

    public string VFSRecordPath { get; set; }

    public void Dispose()
    {
        if (this.Stream is FileStream fileStream)
        {
            fileStream.Close();
            File.Delete(fileStream.Name);
        }
    }

    public void Report(FtpProgress value)
    {
        this.SyncRecord.Progress = value.Progress;
        this.SyncRecord.Speed = value.TransferSpeed;
        this.SyncRecord.TimeSpan = value.ETA;
    }
}

public abstract class NodeFileProcessContext : IAsyncDisposable
{
    public abstract ValueTask DisposeAsync();
    public abstract ValueTask ProcessAsync(NodeFileUploadContext nodeFileUploadContext, CancellationToken cancellationToken = default);
}

public class NodeFileFtpProcessContext : NodeFileProcessContext
{
    public NodeFileFtpProcessContext(NodeInfoModel nodeInfo,
                                        FtpConfigModel ftpConfiguration)
    {
        NodeInfo = nodeInfo;
        FtpConfiguration = ftpConfiguration;
    }

    public NodeInfoModel NodeInfo { get; init; }
    public FtpConfigModel FtpConfiguration { get; init; }

    private AsyncFtpClient _asyncFtpClient;

    public override async ValueTask DisposeAsync()
    {
        await DisposeFtpClient();
    }

    private async ValueTask DisposeFtpClient()
    {
        if (this._asyncFtpClient == null)
        {
            return;
        }
        await _asyncFtpClient.Disconnect();
        _asyncFtpClient.Dispose();
    }

    public override async ValueTask ProcessAsync(NodeFileUploadContext nodeFileUploadContext, CancellationToken cancellationToken = default)
    {

        try
        {
            ArgumentNullException.ThrowIfNull(nodeFileUploadContext);
            if (this._asyncFtpClient == null)
            {
                this._asyncFtpClient = CreateFtpClient(this.FtpConfiguration);
            }
            else if (this._asyncFtpClient != null && this._asyncFtpClient.IsConnected)
            {
                await DisposeFtpClient();
                this._asyncFtpClient = CreateFtpClient(this.FtpConfiguration);
            }
            await _asyncFtpClient.AutoConnect(cancellationToken);
            nodeFileUploadContext.SyncRecord.UtcBeginTime = DateTime.UtcNow;
            nodeFileUploadContext.SyncRecord.Status = NodeFileSyncStatus.Processing;
            var ftpStatus = await this._asyncFtpClient.UploadStream(
                nodeFileUploadContext.Stream,
                nodeFileUploadContext.SyncRecord.StoragePath,
                FtpRemoteExists.Overwrite,
                true,
                nodeFileUploadContext,
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

    private AsyncFtpClient CreateFtpClient(FtpConfigModel ftpConfig)
    {
        var ftpClient = new AsyncFtpClient(ftpConfig.Host, ftpConfig.Username, ftpConfig.Password, ftpConfig.Port,
            new FtpConfig()
            {
                ConnectTimeout = ftpConfig.ConnectTimeout,
                ReadTimeout = ftpConfig.ReadTimeout,
                DataConnectionReadTimeout = ftpConfig.DataConnectionReadTimeout,
                DataConnectionConnectTimeout = ftpConfig.DataConnectionConnectTimeout,
                DataConnectionType = (FtpDataConnectionType)ftpConfig.DataConnectionType
            });
        return ftpClient;
    }
}

public class NodeFileSystemUploadService : BackgroundService
{
    readonly ILogger<NodeFileSystemUploadService> _logger;
    readonly ExceptionCounter _exceptionCounter;
    readonly BatchQueue<BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncRecord>> _fileSyncOperationQueueBatchQueue;
    readonly BatchQueue<BatchQueueOperation<NodeFileSystemInfoEvent, bool>> _nodeFileSystemEventQueue;
    readonly ApplicationRepositoryFactory<FtpConfigModel> _ftpConfigRepoFactory;
    readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepoFactory;
    readonly ApplicationRepositoryFactory<NodeFileSyncRecordModel> _nodeFileSyncRecordRepoFactory;

    public NodeFileSystemUploadService(
        ILogger<NodeFileSystemUploadService> logger,
        ExceptionCounter exceptionCounter,
        BatchQueue<BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncRecord>> fileSyncBatchQueue,
        BatchQueue<BatchQueueOperation<NodeFileSystemInfoEvent, bool>> nodeFileSystemEventQueue,
        ApplicationRepositoryFactory<FtpConfigModel> ftpConfigRepoFactory,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepoFactory,
        ApplicationRepositoryFactory<NodeFileSyncRecordModel> nodeFileSyncRecordRepoFactory
    )
    {
        _logger = logger;
        _exceptionCounter = exceptionCounter;
        _fileSyncOperationQueueBatchQueue = fileSyncBatchQueue;
        _nodeFileSystemEventQueue = nodeFileSystemEventQueue;
        _ftpConfigRepoFactory = ftpConfigRepoFactory;
        _nodeInfoRepoFactory = nodeInfoRepoFactory;
        _nodeFileSyncRecordRepoFactory = nodeFileSyncRecordRepoFactory;
    }

    NodeFileUploadGroupKey NodeFileGroup(BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncRecord> op)
    {
        return new NodeFileUploadGroupKey()
        {
            ConfigurationId = op.Argument.ConfigurationId,
            ConfigurationProtocol = op.Argument.ConfigurationProtocol,
            NodeInfoId = op.Argument.NodeInfoId,
            LengthBase = op.Argument.Stream.Length / (1024 * 1024 * 100)
        };
    }

    NodeFileSyncRecordModel? CreateSyncRecordModel(BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncRecord> op)
    {
        if (op.Context is not NodeFileUploadContext context)
        {
            return null;
        }
        return new NodeFileSyncRecordModel()
        {
            Id = Guid.NewGuid().ToString(),
            CreationDateTime = DateTime.UtcNow,
            Value = context.SyncRecord
        };
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await foreach (var array in _fileSyncOperationQueueBatchQueue.ReceiveAllAsync(cancellationToken))
        {
            try
            {
                var nodeFileGroupArray = array.GroupBy(NodeFileGroup).ToArray();
                await Parallel.ForEachAsync(nodeFileGroupArray, 
                        new ParallelOptions()
                        {
                            CancellationToken = cancellationToken,
                            MaxDegreeOfParallelism = 4,
                        },
                      ProcessBatchQueueOperationAsync
                      );
                using var nodeFileSyncRecordRepo = _nodeFileSyncRecordRepoFactory.CreateRepository();
                var syncRecords = nodeFileGroupArray.SelectMany(x => x).Select(CreateSyncRecordModel).Where(x => x is not null);
                await nodeFileSyncRecordRepo.AddRangeAsync(syncRecords, cancellationToken);
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
        }
    }

     async ValueTask ProcessBatchQueueOperationAsync(
        IGrouping<NodeFileUploadGroupKey, BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncRecord>> opGroup,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var key = opGroup.Key;
            var nodeInfo = await FindNodeInfoAsync(key.NodeInfoId, cancellationToken);
            if (nodeInfo == null)
            {
                BatchFail(
                    opGroup,
                    -1,
                    "invalid node info id");
                return;
            }
            switch (key.ConfigurationProtocol)
            {
                case NodeFileSyncConfigurationProtocol.Unknown:
                    BatchFail(
                        opGroup,
                        -1,
                        "unknown protocol");
                    break;
                case NodeFileSyncConfigurationProtocol.Ftp:
                    await ProcessBatchOperationsByFtpProtocolAsync(
                        key.ConfigurationId,
                        nodeInfo,
                        opGroup,
                        cancellationToken);
                    break;
                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            BatchFail(opGroup, ex.HResult, ex.Message);
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }


    }

    async Task<NodeInfoModel?> FindNodeInfoAsync(string nodeInfoId, CancellationToken cancellationToken = default)
    {
        using var nodeInfoRepo = _nodeInfoRepoFactory.CreateRepository();
        var nodeInfo = await nodeInfoRepo.GetByIdAsync(nodeInfoId, cancellationToken);
        return nodeInfo;
    }

    static void BatchFail(
        IEnumerable<BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncRecord>> opGroup,
        int errorCode,
        string message)
    {
        foreach (var op in opGroup)
        {
            if (op.Context is not NodeFileUploadContext uploadContext)
            {
                continue;
            }
            var syncRecord = uploadContext.SyncRecord;
            syncRecord.ErrorCode = errorCode;
            syncRecord.Message = message;
            syncRecord.Status = NodeFileSyncStatus.Faulted;
            op.SetResult(syncRecord);
        }
    }

    async ValueTask ProcessBatchOperationsByFtpProtocolAsync(
        string configurationId,
        NodeInfoModel nodeInfo,
        IEnumerable<BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncRecord>> batchQueueOperations,
        CancellationToken cancellationToken)
    {
        var ftpConfig = await FindFtpConfigurationAsync(configurationId, cancellationToken);

        if (ftpConfig == null)
        {
            BatchFail(batchQueueOperations, -1, "invalid ftp configuration id");
            return;
        }

        await using var nodeFileFtpProcessContext = new NodeFileFtpProcessContext(nodeInfo, ftpConfig);

        foreach (var op in batchQueueOperations)
        {
            await ProcessContextAsync(nodeInfo, nodeFileFtpProcessContext, op, cancellationToken);
        }

    }

    private async Task ProcessContextAsync(
        NodeInfoModel nodeInfo,
        NodeFileFtpProcessContext nodeFileFtpProcessContext,
        BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncRecord> op,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (op.Context is not NodeFileUploadContext nodeFileUploadContext)
            {
                return;
            }
            var syncRecord = nodeFileUploadContext.SyncRecord;
            try
            {
                if (nodeFileUploadContext == null)
                {
                    return;
                }

                await nodeFileFtpProcessContext.ProcessAsync(
                    nodeFileUploadContext,
                    cancellationToken);

                var nodeFilePath = NodeFileSystemHelper.GetNodeFilePath(
                    nodeInfo.Name,
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
                op.SetResult(syncRecord);
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