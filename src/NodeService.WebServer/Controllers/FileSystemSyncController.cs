using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Models;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.VirtualSystem;

namespace NodeService.WebServer.Controllers
{
    [ApiController]
    [Route("api/[controller]/[action]")]
    public class FileSystemSyncController
    {
        readonly ILogger<FileSystemSyncController> _logger;
        readonly ExceptionCounter _exceptionCounter;
        readonly BatchQueue<BatchQueueOperation<FileSystemSyncRequest, FileSystemSyncResponse>> _fileSyncBatchQueue;

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
        public async Task<ApiResponse<FileSystemSyncResponse>> UploadAsync([FromForm] FileSystemSyncRequest fileSyncRequest, CancellationToken cancellationToken = default)
        {
            var rsp = new ApiResponse<FileSystemSyncResponse>();
            try
            {
                ArgumentException.ThrowIfNullOrEmpty(fileSyncRequest.NodeId, nameof(fileSyncRequest.NodeId));
                ArgumentException.ThrowIfNullOrEmpty(fileSyncRequest.TargetDirectory, nameof(fileSyncRequest.TargetDirectory));
                ArgumentException.ThrowIfNullOrEmpty(fileSyncRequest.FtpConfigId, nameof(fileSyncRequest.FtpConfigId));
                if (fileSyncRequest.Files.Count == 0)
                {
                    rsp.SetResult(new FileSystemSyncResponse()
                    {
                        Progresses = []
                    });
                }
                else
                {
                    var op = new BatchQueueOperation<FileSystemSyncRequest, FileSystemSyncResponse>(
                        fileSyncRequest,
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
    }
}
