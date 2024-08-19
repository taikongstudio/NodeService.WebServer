using Microsoft.Extensions.DependencyInjection;
using NodeService.Infrastructure.NodeFileSystem;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.DataServices;

namespace NodeService.WebServer.Services.NodeFileSystem;

public class FtpClientProcessContext : ProcessContext
{
    readonly NodeInfoQueryService _nodeQueryService;
    readonly FtpClientFactory _ftpClientFactory;
    readonly ObjectCache _objectCache;
    readonly ILogger<FtpClientProcessContext> _logger;
    readonly SyncRecordQueryService _syncRecordQueryService;
    readonly ExceptionCounter _exceptionCounter;
    readonly IServiceProvider _serviceProvider;
    readonly ConfigurationQueryService _configurationQueryService;

    [ActivatorUtilitiesConstructor]
    public FtpClientProcessContext(
        ILogger<FtpClientProcessContext> logger,
        NodeInfoQueryService nodeQueryService,
        ExceptionCounter exceptionCounter,
        FtpClientFactory ftpClientFactory,
        ConfigurationQueryService configurationQueryService,
        SyncRecordQueryService syncRecordQueryService,
        ObjectCache  objectCache)
    {
        _configurationQueryService = configurationQueryService;
        _exceptionCounter = exceptionCounter;
        _logger = logger;
        _syncRecordQueryService = syncRecordQueryService;
        _nodeQueryService = nodeQueryService;
        _ftpClientFactory = ftpClientFactory;
        _objectCache = objectCache;
    }


    public override async ValueTask ProcessAsync(NodeFileSyncContext syncContext, CancellationToken cancellationToken = default)
    {
        try
        {
            var nodeInfo = await _nodeQueryService.QueryNodeInfoByIdAsync(
                syncContext.Request.NodeInfoId,
                true,
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

            if (!rsp.HasValue || ftpConfig?.Value == null)
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
            await using var ftpClient = await _ftpClientFactory.CreateClientAsync(
                ftpConfig.Value,
                TimeSpan.FromSeconds(30),
                cancellationToken);
            if (!ftpClient.IsConnected)
            {
                await ftpClient.AutoConnect(cancellationToken);
            }
            var ftpObjectInfo = await ftpClient.GetObjectInfo(syncContext.Record.StoragePath, true, cancellationToken);
            NodeFileInfo? ftpNodeFileInfo = null;
            if (ftpObjectInfo == null)
            {
                syncContext.IsPhysicialStorageNotExists = true;
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
                syncContext.Record.UtcEndTime = DateTime.UtcNow;
            }
            var fileInfoCacheKey = NodeFileSyncHelper.BuiIdCacheKey(ftpConfig.Value.Host,
                ftpConfig.Value.Port,
                syncContext.Request.StoragePath);
            await _objectCache.SetObjectAsync(
                fileInfoCacheKey,
                new FileInfoCache()
                {
                    CreateDateTime = DateTime.UtcNow,
                    DateTime = syncContext.Record.FileInfo.LastWriteTime,
                    FullName = syncContext.Record.FullName,
                    Length = syncContext.Record.FileInfo.Length,
                    ModifiedDateTime = DateTime.UtcNow,
                },
                cancellationToken);
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
            if (syncContext.Record.Status != NodeFileSyncStatus.Skipped)
            {
                await AddOrUpdateSyncRecordAsync(
                    syncContext,
                    cancellationToken);
            }
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


}
