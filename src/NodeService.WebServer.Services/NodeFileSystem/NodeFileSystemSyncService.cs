using FluentFTP;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Models;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.VirtualSystem;
using Org.BouncyCastle.Crypto.IO;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace NodeService.WebServer.Services.NodeFileSystem;

public class NodeFileSystemSyncService : BackgroundService, IProgress<FtpProgress>
{
    private class FormFileUploadContext
    {
        public FileSystemFileSyncInfo SyncInfo { get; set; }

        public FileSystemSyncProgress Progress { get; set; }

        public AsyncFtpClient FtpClient { get; set; }
    }


    private class NodeFtpContext : IAsyncDisposable
    {
        public NodeInfoModel NodeInfo { get; set; }
        public FtpConfigModel FtpConfig { get; set; }

        public AsyncFtpClient AsyncFtpClient { get; set; }

        public void Dispose()
        {
            if (AsyncFtpClient != null && !AsyncFtpClient.IsDisposed) AsyncFtpClient.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            return ((IAsyncDisposable)AsyncFtpClient).DisposeAsync();
        }
    }


    private readonly ILogger<NodeFileSystemSyncService> _logger;
    private readonly ExceptionCounter _exceptionCounter;
    private readonly BatchQueue<BatchQueueOperation<FileSystemSyncRequest, FileSystemSyncResponse>> _fileSyncBatchQueue;
    private readonly ApplicationRepositoryFactory<FtpConfigModel> _ftpConfigRepoFactory;
    private readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepoFactory;
    private readonly ConcurrentDictionary<string, FileSystemSyncProgress> _progressesDictionary;

    public NodeFileSystemSyncService(
        ILogger<NodeFileSystemSyncService> logger,
        ExceptionCounter exceptionCounter,
        BatchQueue<BatchQueueOperation<FileSystemSyncRequest, FileSystemSyncResponse>> fileSyncBatchQueue,
        ApplicationRepositoryFactory<FtpConfigModel> ftpConfigRepoFactory,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepoFactory
    )
    {
        _logger = logger;
        _exceptionCounter = exceptionCounter;
        _fileSyncBatchQueue = fileSyncBatchQueue;
        _ftpConfigRepoFactory = ftpConfigRepoFactory;
        _nodeInfoRepoFactory = nodeInfoRepoFactory;
        _progressesDictionary = new ConcurrentDictionary<string, FileSystemSyncProgress>();
    }

    public void Report(FtpProgress ftpProgress)
    {
        if (!_progressesDictionary.TryGetValue(ftpProgress.RemotePath, out var fileSystemSyncProgress)) return;
        fileSystemSyncProgress.Progress = ftpProgress.Progress;
        fileSystemSyncProgress.Speed = ftpProgress.TransferSpeed;
        fileSystemSyncProgress.TimeSpan = ftpProgress.ETA;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await foreach (var arrayPoolCollection in _fileSyncBatchQueue.ReceiveAllAsync(cancellationToken))
            try
            {
                using var nodeInfoRepo = _nodeInfoRepoFactory.CreateRepository();
                using var ftpConfigRepo = _ftpConfigRepoFactory.CreateRepository();
                foreach (var batchQueueOperationList in arrayPoolCollection.GroupBy(GroupFunc))
                {
                    var (nodeId, ftpConfigId) = batchQueueOperationList.Key;
                    await ProcessBatchQueueOperationListAsync(
                        nodeInfoRepo,
                        ftpConfigRepo,
                        nodeId,
                        ftpConfigId,
                        batchQueueOperationList,
                        cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
    }

    private async ValueTask ProcessBatchQueueOperationListAsync(
        IRepository<NodeInfoModel> nodeInfoRepo,
        IRepository<FtpConfigModel> ftpConfigRepo,
        string nodeId,
        string ftpConfigId,
        IEnumerable<BatchQueueOperation<FileSystemSyncRequest, FileSystemSyncResponse>> batchQueueOperationGroup,
        CancellationToken cancellationToken = default)
    {
        var nodeInfo = await nodeInfoRepo.GetByIdAsync(nodeId, cancellationToken);
        if (nodeInfo == null)
        {
            await ParallelProcessBatchQueueOperationListAsync(
                batchQueueOperationGroup,
                NodeInfoNotFound,
                null,
                cancellationToken);
            return;
        }

        var ftpConfig = await ftpConfigRepo.GetByIdAsync(ftpConfigId, cancellationToken);
        if (ftpConfig == null)
        {
            await ParallelProcessBatchQueueOperationListAsync(
                batchQueueOperationGroup,
                FtpConfigNotFound,
                null,
                cancellationToken);
            return;
        }

        await using var nodeFtpContext = new NodeFtpContext()
        {
            NodeInfo = nodeInfo,
            FtpConfig = ftpConfig,
            AsyncFtpClient = CreateFtpClient(ftpConfig)
        };
        await ParallelProcessBatchQueueOperationListAsync(
            batchQueueOperationGroup,
            ProcessBatchQueueOperationAsync,
            nodeFtpContext,
            cancellationToken);
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

    private async ValueTask NodeInfoNotFound(
        BatchQueueOperation<FileSystemSyncRequest, FileSystemSyncResponse> batchQueueOperation,
        CancellationToken cancellationToken = default
    )
    {
        var fileSyncResponse = new FileSystemSyncResponse();
        foreach (var formFile in batchQueueOperation.Argument.FileList)
        {
            var progress = new FileSystemSyncProgress()
            {
                Directory = batchQueueOperation.Argument.TargetDirectory,
                FileName = formFile.FileName,
                NodeId = batchQueueOperation.Argument.NodeId,
                Status = "Unknown"
            };
            fileSyncResponse.Progresses.Add(progress);
        }

        fileSyncResponse.ErrorCode = -1;
        fileSyncResponse.Message = $"Node info not found";
        batchQueueOperation.SetResult(fileSyncResponse);
    }

    private async ValueTask FtpConfigNotFound(
        BatchQueueOperation<FileSystemSyncRequest, FileSystemSyncResponse> batchQueueOperation,
        CancellationToken cancellationToken = default
    )
    {
        var fileSyncResponse = new FileSystemSyncResponse();
        foreach (var formFile in batchQueueOperation.Argument.FileList)
        {
            var progress = new FileSystemSyncProgress()
            {
                Directory = batchQueueOperation.Argument.TargetDirectory,
                FileName = formFile.FileName,
                NodeId = batchQueueOperation.Argument.NodeId,
                Status = "Unknown"
            };
            fileSyncResponse.Progresses.Add(progress);
        }

        fileSyncResponse.ErrorCode = -1;
        fileSyncResponse.Message = $"Ftp configuration not found";
        batchQueueOperation.SetResult(fileSyncResponse);
    }

    private async ValueTask ParallelProcessBatchQueueOperationListAsync(
        IEnumerable<BatchQueueOperation<FileSystemSyncRequest, FileSystemSyncResponse>> batchQueueOperationList,
        Func<BatchQueueOperation<FileSystemSyncRequest, FileSystemSyncResponse>, CancellationToken, ValueTask> func,
        object? context,
        CancellationToken cancellationToken = default)
    {
        foreach (var batchQueueOperation in batchQueueOperationList) batchQueueOperation.Context = context;
        await Parallel.ForEachAsync(batchQueueOperationList, new ParallelOptions()
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = 4
        }, func);
    }

    private async ValueTask ProcessBatchQueueOperationAsync(
        BatchQueueOperation<FileSystemSyncRequest, FileSystemSyncResponse> batchQueueOperation,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (batchQueueOperation.Context is not NodeFtpContext nodeFtpContext) return;
            var ftpConfig = nodeFtpContext.FtpConfig;
            var ftpClient = nodeFtpContext.AsyncFtpClient;
            await ftpClient.AutoConnect(cancellationToken);
            var fileSystemSyncResponse = new FileSystemSyncResponse();
            foreach (var fileSyncInfo in batchQueueOperation.Argument.FileSyncInfoList)
                try
                {
                    var formFileUploadContext = CreateFormFileUploadContext(batchQueueOperation.Argument, fileSyncInfo);
                    formFileUploadContext.FtpClient = ftpClient;
                    fileSystemSyncResponse.Progresses.Add(formFileUploadContext.Progress);
                    await ProcessFormFileUploadContextAsync(formFileUploadContext, cancellationToken);
                    await FinializeFormFileUploadContextAsync(formFileUploadContext, cancellationToken);
                }
                catch (Exception ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }

            batchQueueOperation.SetResult(fileSystemSyncResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
        }
    }

    private FormFileUploadContext CreateFormFileUploadContext(FileSystemSyncRequest request,
        FileSystemFileSyncInfo info)
    {
        var fileSystemSyncProgress = new FileSystemSyncProgress()
        {
            Id = Guid.NewGuid().ToString(),
            Name = info.FileName,
            ConfigurationId = request.FileSystemWatchConfigurationId,
            NodeId = request.NodeId,
            Directory = request.TargetDirectory,
            FileName = info.FileName,
            Status = "Unknown"
        };

        var context = new FormFileUploadContext()
        {
            Progress = fileSystemSyncProgress,
            SyncInfo = info
        };

        return context;
    }

    private ValueTask FinializeFormFileUploadContextAsync(
        FormFileUploadContext formFileUploadContext,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    private async ValueTask ProcessFormFileUploadContextAsync(
        FormFileUploadContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _progressesDictionary.TryAdd(context.SyncInfo.TargetFilePath, context.Progress);
            context.Progress.BeginUtc = DateTime.UtcNow;
            var ftpStatus = await context.FtpClient.UploadStream(
                context.SyncInfo.Stream,
                context.SyncInfo.TargetFilePath,
                FtpRemoteExists.Overwrite,
                true,
                this,
                cancellationToken);
            context.Progress.Status = ftpStatus.ToString();
        }
        catch (Exception ex)
        {
            context.Progress.ErrorCode = ex.HResult;
            context.Progress.Message = ex.Message;
            context.Progress.Message = "Fault";
        }
        finally
        {
            context.Progress.EndUtc = DateTime.UtcNow;
            if (context.SyncInfo.IsCompressed && context.SyncInfo.Stream is MemoryStream memoryStream)
            {
                var array = memoryStream.GetBuffer();
                ArrayPool<byte>.Shared.Return(array, true);
            }

            if (context.SyncInfo.TargetFilePath != null)
                _progressesDictionary.TryRemove(context.SyncInfo.TargetFilePath, out _);
        }
    }

    private (string NodeId, string FtpConfigId) GroupFunc(
        BatchQueueOperation<FileSystemSyncRequest, FileSystemSyncResponse> batchQueueOperation)
    {
        return (batchQueueOperation.Argument.NodeId, batchQueueOperation.Argument.FtpConfigId);
    }
}