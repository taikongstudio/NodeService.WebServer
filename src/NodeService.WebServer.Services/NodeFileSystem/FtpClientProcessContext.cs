using NodeService.Infrastructure.NodeFileSystem;

namespace NodeService.WebServer.Services.NodeFileSystem;

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
            nodeFileUploadContext.SyncRecord.Message = ex.ToString();
        }
        finally
        {
            nodeFileUploadContext.SyncRecord.UtcEndTime = DateTime.UtcNow;
        }
    }

    public override async ValueTask IdleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _asyncFtpClient.IsStillConnected(token: cancellationToken);
        }
        catch (Exception ex)
        {

        }
    }
}
