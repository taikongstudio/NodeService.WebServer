using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.NodeFileSystem;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;

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
        if (this._asyncFtpClient == null)
        {
            return;
        }
        await _asyncFtpClient.Disconnect();
        _asyncFtpClient.Dispose();
        await Task.CompletedTask;
    }

    public override ValueTask ProcessAsync(NodeFileUploadContext nodeFileUploadContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodeFileUploadContext);
        return ProcessCoreAsync(nodeFileUploadContext, cancellationToken);
    }

    private async ValueTask ProcessCoreAsync(
    NodeFileUploadContext context,
    CancellationToken cancellationToken = default)
    {
        try
        {
            if (this._asyncFtpClient == null)
            {
                this._asyncFtpClient = CreateFtpClient(this.FtpConfiguration);
            }
            await this._asyncFtpClient.AutoConnect(cancellationToken);
            context.SyncRecord.UtcBeginTime = DateTime.UtcNow;
            context.SyncRecord.Status = NodeFileSyncStatus.Processing;
            var ftpStatus = await this._asyncFtpClient.UploadStream(
                context.Stream,
                context.SyncRecord.StoragePath,
                FtpRemoteExists.Overwrite,
                true,
                context,
                cancellationToken);
            context.SyncRecord.Status = NodeFileSyncStatus.Processed;
        }
        catch (Exception ex)
        {
            context.SyncRecord.Status = NodeFileSyncStatus.Faulted;
            context.SyncRecord.ErrorCode = ex.HResult;
            context.SyncRecord.Message = ex.Message;
        }
        finally
        {
            context.SyncRecord.UtcEndTime = DateTime.UtcNow;
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
    private readonly ILogger<NodeFileSystemUploadService> _logger;
    private readonly ExceptionCounter _exceptionCounter;
    private readonly BatchQueue<BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncResponse>> _fileSyncBatchQueue;
    private readonly ApplicationRepositoryFactory<FtpConfigModel> _ftpConfigRepoFactory;
    private readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepoFactory;

    public NodeFileSystemUploadService(
        ILogger<NodeFileSystemUploadService> logger,
        ExceptionCounter exceptionCounter,
        BatchQueue<BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncResponse>> fileSyncBatchQueue,
        ApplicationRepositoryFactory<FtpConfigModel> ftpConfigRepoFactory,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepoFactory
    )
    {
        _logger = logger;
        _exceptionCounter = exceptionCounter;
        _fileSyncBatchQueue = fileSyncBatchQueue;
        _ftpConfigRepoFactory = ftpConfigRepoFactory;
        _nodeInfoRepoFactory = nodeInfoRepoFactory;
    }

    NodeFileUploadGroupKey Group(BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncResponse> op)
    {
        return new NodeFileUploadGroupKey()
        {
            ConfigurationId = op.Argument.FileInfo.ConfigurationId,
            ConfigurationProtocol = op.Argument.FileInfo.ConfigurationProtocol,
            NodeInfoId = op.Argument.FileInfo.NodeInfoId,
            LengthBase = op.Argument.Stream.Length / (1024 * 1024 * 100)
        };
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await foreach (var array in _fileSyncBatchQueue.ReceiveAllAsync(cancellationToken))
        {
            try
            {
                await Parallel.ForEachAsync(array.GroupBy(Group).ToArray()
                      , new ParallelOptions()
                      {
                          CancellationToken = cancellationToken,
                          MaxDegreeOfParallelism = 4,
                      },
                      ProcessBatchQueueOperationAsync
                      );
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
        }
    }

    private async ValueTask ProcessBatchQueueOperationAsync(
        IGrouping<NodeFileUploadGroupKey, BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncResponse>> opGroup,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var key = opGroup.Key;
            var nodeInfo = await FindNodeInfoAsync(key.NodeInfoId, cancellationToken);
            if (nodeInfo == null)
            {
                BatchFail(opGroup, -1, "invalid node info id");
                return;
            }
            var vfsRoot = VirtualFileSystemHelper.GetRootDirectory();
            var nodeName = nodeInfo.Name;
            var nodeRoot = VirtualFileSystemHelper.GetNodeRoot(nodeName);
            if (!Directory.Exists(nodeRoot))
            {
                Directory.CreateDirectory(nodeRoot);
            }
            switch (key.ConfigurationProtocol)
            {
                case NodeFileSyncConfigurationProtocol.Unknown:
                    BatchFail(opGroup, -1, "unknown protocol");
                    break;
                case NodeFileSyncConfigurationProtocol.Ftp:
                    await ProcessBatchOperationsByFtpProtocolAsync(
                        nodeRoot,
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
        IEnumerable<BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncResponse>> opGroup,
        int errorCode,
        string message)
    {
        foreach (var op in opGroup)
        {
            var uploadContext = op.Context as NodeFileUploadContext;
            var syncRecord = uploadContext.SyncRecord;
            syncRecord.ErrorCode = errorCode;
            syncRecord.Message = message;
            syncRecord.Status = NodeFileSyncStatus.Faulted;
            op.SetResult(new NodeFileSyncResponse()
            {
                SyncRecord = syncRecord,
            });
        }
    }

    async ValueTask ProcessBatchOperationsByFtpProtocolAsync(
        string nodeRoot,
        string configurationId,
        NodeInfoModel nodeInfo,
        IEnumerable<BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncResponse>> batchQueueOperations,
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
            try
            {
                var fileSystemSyncResponse = new NodeFileSyncResponse();
                var nodeFileUploadContext = op.Context as NodeFileUploadContext;
                fileSystemSyncResponse.SyncRecord = nodeFileUploadContext == null ?
                    op.Argument.FileInfo.CreateNodeFileSyncRecord() : nodeFileUploadContext.SyncRecord;
                try
                {
                    if (nodeFileUploadContext == null)
                    {
                        continue;
                    }

                    var fullName = nodeFileUploadContext.SyncRecord.FileInfo.FullName;
                    var directroy = Path.GetDirectoryName(fullName);
                    if (directroy == null)
                    {
                        continue;
                    }
                    directroy = directroy.Replace(":\\", "\\");
                    directroy = Path.Combine(nodeRoot, directroy);
                    if (!Directory.Exists(directroy))
                    {
                        Directory.CreateDirectory(directroy);
                    }
                    var fileName = Path.GetFileName(fullName);
                    var filePath = Path.Combine(directroy, fileName + ".json");

                    nodeFileUploadContext.VFSRecordPath = filePath;

                    File.WriteAllText(filePath, JsonSerializer.Serialize(fileSystemSyncResponse.SyncRecord));

                    await nodeFileFtpProcessContext.ProcessAsync(nodeFileUploadContext, cancellationToken);
                }
                catch (Exception ex)
                {
                    fileSystemSyncResponse.SyncRecord.Status = NodeFileSyncStatus.Faulted;
                    fileSystemSyncResponse.SyncRecord.ErrorCode = ex.HResult;
                    fileSystemSyncResponse.SyncRecord.Message = ex.Message;
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }
                finally
                {
                    op.SetResult(fileSystemSyncResponse);
                    File.WriteAllText(nodeFileUploadContext.VFSRecordPath, JsonSerializer.Serialize(fileSystemSyncResponse.SyncRecord));
                }
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }

        }

    }

    async ValueTask<FtpConfigModel?> FindFtpConfigurationAsync(string configurationId, CancellationToken cancellationToken)
    {
        using var ftpConfigRepo = _ftpConfigRepoFactory.CreateRepository();
        var ftpConfig = await ftpConfigRepo.GetByIdAsync(configurationId, cancellationToken);
        return ftpConfig;
    }
}