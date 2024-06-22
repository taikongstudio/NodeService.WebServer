using NodeService.Infrastructure.Concurrent;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public class FileSystemSyncController : Controller
{
    private readonly ILogger<FileSystemSyncController> _logger;
    private readonly ExceptionCounter _exceptionCounter;
    private readonly BatchQueue<BatchQueueOperation<FileSystemSyncRequest, FileSystemSyncResponse>> _fileSyncBatchQueue;

    public FileSystemSyncController(
        ILogger<FileSystemSyncController> logger,
        ExceptionCounter exceptionCounter,
        BatchQueue<BatchQueueOperation<FileSystemSyncRequest, FileSystemSyncResponse>> fileSyncBatchQueue
    )
    {
        _logger = logger;
        _exceptionCounter = exceptionCounter;
        _fileSyncBatchQueue = fileSyncBatchQueue;
    }

    //[ValidateAntiForgeryToken]
    [DisableRequestSizeLimit]
    [HttpPost("/api/FileSystemSync/Upload")]
    public async Task<ApiResponse<FileSystemSyncResponse>> UploadAsync(
        [FromForm] FileSystemSyncRequest fileSystemSyncRequest,
        CancellationToken cancellationToken = default)
    {
        var rsp = new ApiResponse<FileSystemSyncResponse>();
        try
        {
            var formCollection = await HttpContext.Request.ReadFormAsync();

            ArgumentException.ThrowIfNullOrEmpty(fileSystemSyncRequest.NodeId, nameof(fileSystemSyncRequest.NodeId));
            ArgumentException.ThrowIfNullOrEmpty(fileSystemSyncRequest.TargetDirectory,
                nameof(fileSystemSyncRequest.TargetDirectory));
            ArgumentException.ThrowIfNullOrEmpty(fileSystemSyncRequest.FtpConfigId,
                nameof(fileSystemSyncRequest.FtpConfigId));
            if (fileSystemSyncRequest.FileList.Count == 0)
            {
                rsp.SetResult(new FileSystemSyncResponse()
                {
                    Progresses = []
                });
            }
            else
            {
                foreach (var formFile in fileSystemSyncRequest.FileList)
                    if (TryGetSyncInfoFromHeader(formFile.Headers, out var fileSystemFileSyncInfo) &&
                        fileSystemFileSyncInfo != null)
                    {
                        fileSystemSyncRequest.FileSyncInfoList.Add(fileSystemFileSyncInfo);
                        await fileSystemFileSyncInfo.EnsureStreamFromFormFileAsync(formFile, cancellationToken);
                        break;
                    }

                var op = new BatchQueueOperation<FileSystemSyncRequest, FileSystemSyncResponse>(
                    fileSystemSyncRequest,
                    BatchQueueOperationKind.InsertOrUpdate,
                    BatchQueueOperationPriority.Normal,
                    cancellationToken);
                await _fileSyncBatchQueue.SendAsync(op, cancellationToken);
                var fileSyncResponse = await op.WaitAsync(cancellationToken);
                rsp.SetResult(fileSyncResponse);
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
            rsp.ErrorCode = ex.HResult;
            rsp.Message = ex.Message;
        }

        return rsp;
    }

    private bool TryGetSyncInfoFromHeader(IHeaderDictionary headerDictionary,
        out FileSystemFileSyncInfo? fileSystemFileSyncInfo)
    {
        fileSystemFileSyncInfo = null;
        try
        {
            if (headerDictionary.TryGetValue(nameof(FileSystemFileSyncInfo), out var values))
            {
                var value = values.FirstOrDefault();
                fileSystemFileSyncInfo = JsonSerializer.Deserialize<FileSystemFileSyncInfo>(value);
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }

        return fileSystemFileSyncInfo != null;
    }
}