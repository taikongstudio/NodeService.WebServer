using FluentFTP.Helpers;
using NodeService.Infrastructure.NodeFileSystem;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Services.NodeFileSystem;

public class FtpClientProcessContext : ProcessContext
{

    readonly AsyncFtpClient _asyncFtpClient;
    readonly BatchQueue<NodeFileSyncRecordModel> _actionBlock;
    readonly ILogger<FtpClientProcessContext> _logger;
    readonly ExceptionCounter _exceptionCounter;

    public FtpClientProcessContext(
        ILogger<FtpClientProcessContext> logger,
        ExceptionCounter exceptionCounter,
        AsyncFtpClient asyncFtpClient,
        BatchQueue<NodeFileSyncRecordModel> actionBlock)
    {
        _logger = logger;
        _exceptionCounter = exceptionCounter;
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

    public override async ValueTask ProcessAsync(NodeFileUploadContext uploadContext, CancellationToken cancellationToken = default)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                uploadContext.SyncRecord.Status = NodeFileSyncStatus.Canceled;
                uploadContext.TrySetResult(NodeFileSyncStatus.Canceled);
                return;
            }
            ArgumentNullException.ThrowIfNull(uploadContext);
            if (!_asyncFtpClient.IsConnected)
            {
                await _asyncFtpClient.AutoConnect();
            }
            var ftpObjectInfo = await _asyncFtpClient.GetObjectInfo(
                uploadContext.SyncRecord.StoragePath,
                true);
            NodeFileInfo? ftpNodeFileInfo = null;
            if (ftpObjectInfo == null)
            {
                uploadContext.IsStorageNotExists = true;
            }
            else
            {
                ftpNodeFileInfo = new NodeFileInfo()
                {
                    LastWriteTime = ftpObjectInfo.Modified,
                    Length = ftpObjectInfo.Size
                };
            }

            if (CompareFileInfo(uploadContext.SyncRecord.FileInfo, ftpNodeFileInfo))
            {
                uploadContext.SyncRecord.Status = NodeFileSyncStatus.Skipped;
                uploadContext.TrySetResult(NodeFileSyncStatus.Skipped);
            }
            else
            {
                uploadContext.SyncRecord.UtcBeginTime = DateTime.UtcNow;
                uploadContext.SyncRecord.Status = NodeFileSyncStatus.Processing;
                var lambdaProgress = new LambdaProgress<FtpProgress>((ftpProgress) =>
                {
                    uploadContext.SyncRecord.Progress = ftpProgress.Progress;
                    if (ftpProgress.Progress < 100)
                    {
                        uploadContext.SyncRecord.TransferSpeed = ftpProgress.TransferSpeed;
                    }
                    uploadContext.SyncRecord.EstimatedTimeSpan = ftpProgress.ETA;
                    uploadContext.SyncRecord.TransferredBytes = ftpProgress.TransferredBytes;

                    _actionBlock.Post(uploadContext.SyncRecord);
                });

                var ftpStatus = await _asyncFtpClient.UploadStream(
                    uploadContext.Stream,
                    uploadContext.SyncRecord.StoragePath,
                    FtpRemoteExists.Overwrite,
                    true,
                    lambdaProgress,
                    cancellationToken);
                if (ftpStatus == FtpStatus.Success)
                {
                    await _asyncFtpClient.SetModifiedTime(
                        uploadContext.SyncRecord.StoragePath,
                        uploadContext.SyncRecord.FileInfo.LastWriteTime,
                        cancellationToken);
                    uploadContext.SyncRecord.Status = NodeFileSyncStatus.Processed;
                    uploadContext.TrySetResult(NodeFileSyncStatus.Processed);
                }
                else
                {
                    uploadContext.SyncRecord.Status = NodeFileSyncStatus.Faulted;
                    uploadContext.TrySetResult(NodeFileSyncStatus.Faulted);
                }
            }
        }
        catch (Exception ex)
        {
            uploadContext.SyncRecord.Status = NodeFileSyncStatus.Faulted;
            uploadContext.SyncRecord.ErrorCode = ex.HResult;
            uploadContext.SyncRecord.Message = ex.ToString();
            uploadContext.TrySetException(ex);
            _logger.LogError(ex.ToString());
            _exceptionCounter.AddOrUpdate(ex, uploadContext.SyncRecord.NodeInfoId);
        }
        finally
        {
            uploadContext.SyncRecord.UtcEndTime = DateTime.UtcNow;
        }
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

    public override async ValueTask<bool> IdleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_asyncFtpClient.IsConnected)
            {
                var workingDirectory = await _asyncFtpClient.GetWorkingDirectory(cancellationToken);
                await _asyncFtpClient.DirectoryExists(workingDirectory, cancellationToken);
                return true;
            }
            else
            {
                await _asyncFtpClient.AutoConnect(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex, _asyncFtpClient.Host);
            _logger.LogError(ex.ToString());
        }
        return false;
    }
}
