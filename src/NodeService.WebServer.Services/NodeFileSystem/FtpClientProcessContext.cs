using Microsoft.Extensions.DependencyInjection;
using NodeService.Infrastructure.NodeFileSystem;
using NodeService.WebServer.Data.Entities;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.DataQueue;
using System;

namespace NodeService.WebServer.Services.NodeFileSystem;

public class FtpClientProcessContext : ProcessContext
{
    readonly IDbContextFactory<InMemoryDbContext> _dbContextFactory;
    readonly NodeInfoQueryService _nodeQueryService;
    readonly ILogger<FtpClientProcessContext> _logger;
    readonly SyncRecordQueryService _syncRecordQueryService;
    readonly ExceptionCounter _exceptionCounter;
    readonly IServiceProvider _serviceProvider;
    readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepoFactory;
    readonly ConfigurationQueryService _configurationQueryService;

    [ActivatorUtilitiesConstructor]
    public FtpClientProcessContext(
        ILogger<FtpClientProcessContext> logger,
        NodeInfoQueryService nodeQueryService,
        ExceptionCounter exceptionCounter,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepoFactory,
        ConfigurationQueryService configurationQueryService,
        IDbContextFactory<InMemoryDbContext> dbContextFactory,
       SyncRecordQueryService syncRecordQueryService)
    {
        _nodeInfoRepoFactory = nodeInfoRepoFactory;
        _configurationQueryService = configurationQueryService;
        _exceptionCounter = exceptionCounter;
        _logger = logger;
        _syncRecordQueryService = syncRecordQueryService;
        _dbContextFactory = dbContextFactory;
        _nodeQueryService = nodeQueryService;
    }

    AsyncFtpClient CreateFtpClient(FtpConfiguration ftpConfig)
    {
        var asyncFtpClient = new AsyncFtpClient(ftpConfig.Host, ftpConfig.Username, ftpConfig.Password, ftpConfig.Port,
                            new FtpConfig()
                            {
                                ConnectTimeout = ftpConfig.ConnectTimeout,
                                ReadTimeout = ftpConfig.ReadTimeout,
                                DataConnectionReadTimeout = ftpConfig.DataConnectionReadTimeout,
                                DataConnectionConnectTimeout = ftpConfig.DataConnectionConnectTimeout,
                                DataConnectionType = (FtpDataConnectionType)ftpConfig.DataConnectionType
                            });
        return asyncFtpClient;
    }

    public override async ValueTask ProcessAsync(NodeFileSyncContext syncContext, CancellationToken cancellationToken = default)
    {
        try
        {
            var nodeInfo = await _nodeQueryService.QueryNodeInfoByIdAsync(
                syncContext.Request.NodeInfoId,
                cancellationToken);
            if (nodeInfo == null)
            {
                syncContext.Record.ErrorCode = -1;
                syncContext.Record.Message = "node info not found";
                syncContext.Record.Status = NodeFileSyncStatus.Faulted;
                return;
            }

            var rsp = await _configurationQueryService.QueryConfigurationByIdListAsync<FtpConfigModel>(
                [syncContext.Request.ConfigurationId],
                cancellationToken: cancellationToken);

            var ftpConfig = rsp.Items?.FirstOrDefault();

            if (!rsp.HasValue || ftpConfig == null)
            {
                syncContext.Record.ErrorCode = -1;
                syncContext.Record.Message = "ftp configuration not found";
                syncContext.Record.Status = NodeFileSyncStatus.Faulted;
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                syncContext.Record.Status = NodeFileSyncStatus.Canceled;
                syncContext.TrySetResult(NodeFileSyncStatus.Canceled);
                return;
            }
            using var ftpClient = CreateFtpClient(ftpConfig.Value);
            if (!ftpClient.IsConnected)
            {
                await ftpClient.AutoConnect(cancellationToken);
            }
            var ftpObjectInfo = await ftpClient.GetObjectInfo(syncContext.Record.StoragePath, true, cancellationToken);
            NodeFileInfo? ftpNodeFileInfo = null;
            if (ftpObjectInfo == null)
            {
                syncContext.IsStorageNotExists = true;
            }
            else
            {
                ftpNodeFileInfo = new NodeFileInfo()
                {
                    LastWriteTime = ftpObjectInfo.Modified,
                    Length = ftpObjectInfo.Size
                };
            }

            if (CompareFileInfo(syncContext.Record.FileInfo, ftpNodeFileInfo))
            {
                syncContext.Record.Status = NodeFileSyncStatus.Skipped;
                syncContext.TrySetResult(NodeFileSyncStatus.Skipped);
            }
            else
            {
                syncContext.Record.UtcBeginTime = DateTime.UtcNow;
                syncContext.Record.Status = NodeFileSyncStatus.Processing;
                await using var progressBlock = new ProgressBlock<FtpProgress>(async (ftpProgresses, token) =>
                  {
                      var ftpProgress = ftpProgresses.LastOrDefault();
                      if (ftpProgress == null)
                      {
                          return;
                      }
                      syncContext.Record.Progress = ftpProgress.Progress;
                      syncContext.Record.TransferSpeed = ftpProgress.TransferSpeed;
                      syncContext.Record.EstimatedTimeSpan = ftpProgress.ETA;
                      syncContext.Record.TransferredBytes = ftpProgress.TransferredBytes;

                      await AddOrUpdateSyncRecordAsync(syncContext, cancellationToken);

                  });

                var ftpStatus = await ftpClient.UploadStream(
                    syncContext.Stream,
                    syncContext.Record.StoragePath,
                    FtpRemoteExists.Overwrite,
                    true,
                    progressBlock,
                    cancellationToken);
                if (ftpStatus == FtpStatus.Success)
                {
                    await ftpClient.SetModifiedTime(
                        syncContext.Record.StoragePath,
                        syncContext.Record.FileInfo.LastWriteTime,
                        cancellationToken);
                    syncContext.Record.Status = NodeFileSyncStatus.Processed;
                    syncContext.TrySetResult(NodeFileSyncStatus.Processed);
                }
                else
                {
                    syncContext.Record.Status = NodeFileSyncStatus.Faulted;
                    syncContext.TrySetResult(NodeFileSyncStatus.Faulted);
                }
            }
        }
        catch (Exception ex)
        {
            syncContext.Record.Status = NodeFileSyncStatus.Faulted;
            syncContext.Record.ErrorCode = ex.HResult;
            syncContext.Record.Message = ex.ToString();
            syncContext.TrySetException(ex);
            _logger.LogError(ex.ToString());
            _exceptionCounter.AddOrUpdate(ex, syncContext.Record.NodeInfoId);
        }
        finally
        {
            syncContext.Record.UtcEndTime = DateTime.UtcNow;
            await CacheHittestResultAsync(
                syncContext,
                cancellationToken);

            await AddOrUpdateSyncRecordAsync(
                syncContext,
                cancellationToken);
        }
    }

    async ValueTask AddOrUpdateSyncRecordAsync(
        NodeFileSyncContext uploadContext,
        CancellationToken cancellationToken)
    {
        await _syncRecordQueryService.AddOrUpdateAsync(uploadContext.Record, cancellationToken);
    }

    static bool CompareFileInfo(NodeFileInfo? oldFileInfo, NodeFileInfo? newFileInfo)
    {
        if (oldFileInfo == null || newFileInfo == null)
        {
            return false;
        }
        return oldFileInfo.Length == newFileInfo.Length &&
                        CompareDateTime(oldFileInfo.LastWriteTime, newFileInfo.LastWriteTime);
        static bool CompareDateTime(DateTime dateTime1, DateTime dateTime2)
        {
            if (dateTime1 == dateTime2)
            {
                return true;
            }
            var dateTimeOffset1 = new DateTimeOffset(dateTime1);
            var dateTimeOffset2 = new DateTimeOffset(dateTime2);
            var diff = dateTimeOffset1.ToUnixTimeSeconds() - dateTimeOffset2.ToUnixTimeSeconds();
            return diff == 0;
        }
    }

    public override ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    async ValueTask CacheHittestResultAsync(
        NodeFileSyncContext syncContext,
        CancellationToken cancellationToken = default)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        var cache = await dbContext.FileHittestResultCacheDbSet.FindAsync(
            [
                syncContext.Record.NodeInfoId,
                syncContext.Record.FileInfo.FullName
            ], cancellationToken);
        if (cache == null)
        {
            cache = new NodeFileHittestResultCache()
            {
                NodeInfoId = syncContext.Record.NodeInfoId,
                FullName = syncContext.Record.FullName,
                DateTime = syncContext.Record.FileInfo.LastWriteTime,
                Length = syncContext.Record.FileInfo.Length,
                CreateDateTime = DateTime.UtcNow,
                ModifiedDateTime = DateTime.UtcNow
            };
            await dbContext.FileHittestResultCacheDbSet.AddAsync(cache, cancellationToken);
        }
        else
        {
            if (syncContext.IsStorageNotExists && syncContext.Record.Status != NodeFileSyncStatus.Processed)
            {
                dbContext.FileHittestResultCacheDbSet.Remove(cache);
            }
            else
            {
                cache.DateTime = syncContext.Record.FileInfo.LastWriteTime;
                cache.Length = syncContext.Record.FileInfo.Length;
                cache.ModifiedDateTime = DateTime.UtcNow;
                dbContext.FileHittestResultCacheDbSet.Update(cache);
            }

        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
