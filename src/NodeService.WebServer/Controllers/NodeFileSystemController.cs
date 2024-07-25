using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using NodeService.Infrastructure.NodeFileSystem;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.DataQueue;
using NodeService.WebServer.Services.NodeFileSystem;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public class NodeFileSystemController : Controller
{
    readonly ILogger<NodeFileSystemController> _logger;
    readonly ExceptionCounter _exceptionCounter;
    readonly IServiceProvider _serviceProvider;
    readonly NodeFileSyncQueueDictionary _batchProcessQueueDictionary;
    readonly SyncRecordQueryService _syncRecordQueryService;
    readonly ObjectCache _objectCache;
    readonly ConfigurationQueryService _configurationQueryService;
    readonly FileInfoCacheService _fileInfoCacheService;

    public NodeFileSystemController(
        ILogger<NodeFileSystemController> logger,
        IServiceProvider serviceProvider,
        ObjectCache distributedCache,
        ExceptionCounter exceptionCounter,
        ConfigurationQueryService configurationQueryService,
        NodeFileSyncQueueDictionary batchProcessQueueDictionary,
        SyncRecordQueryService syncRecordQueryService,
        FileInfoCacheService fileInfoCacheService
    )
    {
        _logger = logger;
        _exceptionCounter = exceptionCounter;
        _serviceProvider = serviceProvider;
        _batchProcessQueueDictionary = batchProcessQueueDictionary;
        _syncRecordQueryService = syncRecordQueryService;
        _objectCache = distributedCache;
        _configurationQueryService = configurationQueryService;
        _fileInfoCacheService = fileInfoCacheService;
    }

    [HttpGet("/api/NodeFileSystem/QueueStatus")]
    public Task<ApiResponse<NodeFileSyncQueueStatus>> QueryQueueStatus()
    {
        var rsp = new ApiResponse<NodeFileSyncQueueStatus>();
        rsp.SetResult(new NodeFileSyncQueueStatus()
        {
            SyncRequestAvaibleCount = 0,
            BatchProcessQueueCount = _batchProcessQueueDictionary.Count,
            BatchProcessQueues = _batchProcessQueueDictionary.Select(x => new BatchProcessQueueInfo()
            {
                QueueId = x.Key,
                QueueName = x.Value.Name,
                CreationDateTime = x.Value.CreationDateTime,
                TotalProcessedCount = x.Value.ProcessedCount,
                QueueCount = x.Value.QueueCount,
                IsConnected = x.Value.IsConnected,
                TotalLengthInQueue = x.Value.GetCurrentTotalLength(),
                AvgProcessTime = x.Value.AvgProcessTime,
                TotalProcessedLength = x.Value.TotalProcessedLength,
                MaxFileLength = x.Value.MaxFileLength,
                MaxFileLengthInQueue = x.Value.MaxFileLengthInQueue,
                MaxProcessTime = x.Value.MaxProcessTime,
                TotalProcessTime = x.Value.TotalProcessTime,
            }).ToArray()
        });
        return Task.FromResult(rsp);
    }

    [HttpGet("/api/NodeFileSystem/SyncRecord/List")]
    public async Task<PaginationResponse<NodeFileSyncRecordModel>> QuerySyncRecordListAsync(
        [FromQuery] QueryNodeFileSystemSyncRecordParameters parameters,
        CancellationToken cancellationToken = default)
    {
        var rsp = new PaginationResponse<NodeFileSyncRecordModel>();
        try
        {
            var list = await _syncRecordQueryService.QueryAsync(parameters, cancellationToken);
            rsp.SetResult(list);
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

    [HttpPost("/api/NodeFileSystem/Hittest")]
    [HttpPost("/api/NodeFileSystem/Cache/Query")]
    public async Task<ApiResponse<FileInfoCacheResult>> HittestAsync(
        NodeFileSyncRequest nodeFileSyncRequest,
        CancellationToken cancellationToken = default)
    {
        var rsp = new ApiResponse<FileInfoCacheResult>();
        try
        {
            var fileInfoCache = await _fileInfoCacheService.GetFileInfoCacheAsync(
                nodeFileSyncRequest.ConfigurationId,
                nodeFileSyncRequest.StoragePath,
                cancellationToken);
            if (fileInfoCache == null)
            {
                rsp.SetResult(FileInfoCacheResult.None);
            }
            else if (
                    nodeFileSyncRequest.FileInfo.FullName == fileInfoCache.FullName
                    &&
                    nodeFileSyncRequest.FileInfo.Length == fileInfoCache.Length
                    &&
                    nodeFileSyncRequest.FileInfo.LastWriteTime == fileInfoCache.DateTime
                    )
            {
                rsp.SetResult(FileInfoCacheResult.Cached);
            }
        }
        catch (Exception ex)
        {
            rsp.ErrorCode = ex.HResult;
            rsp.Message = ex.ToString();
        }
        return rsp;
    }

    [HttpPost("/api/NodeFileSystem/Cache/BulkQuery")]
    public async Task<ApiResponse<BulkQueryFileInfoCacheResult>> BulkQueryFileInfoCacheResultAsync(
    NodeFileSyncRequest[] nodeFileSyncRequests,
    CancellationToken cancellationToken = default)
    {
        var rsp = new ApiResponse<BulkQueryFileInfoCacheResult>();
        try
        {
            var bulkQueryFileInfoCacheResult = new BulkQueryFileInfoCacheResult();
            foreach (var nodeFileSyncRequest in nodeFileSyncRequests)
            {
                var fileInfoCache = await _fileInfoCacheService.GetFileInfoCacheAsync(
    nodeFileSyncRequest.ConfigurationId,
    nodeFileSyncRequest.StoragePath,
    cancellationToken);
                if (fileInfoCache == null)
                {
                    bulkQueryFileInfoCacheResult.Items.Add(new QueryFileInfoCacheResult()
                    {
                        FullPath = nodeFileSyncRequest.FileInfo.FullName,
                        Result = FileInfoCacheResult.None
                    });
                }
                else
                {
                    if (
                        nodeFileSyncRequest.FileInfo.FullName == fileInfoCache.FullName
                        &&
                        nodeFileSyncRequest.FileInfo.Length == fileInfoCache.Length
                        &&
                        nodeFileSyncRequest.FileInfo.LastWriteTime == fileInfoCache.DateTime
                            )
                    {
                        bulkQueryFileInfoCacheResult.Items.Add(new QueryFileInfoCacheResult()
                        {
                            FullPath = nodeFileSyncRequest.FileInfo.FullName,
                            Result = FileInfoCacheResult.Cached
                        });
                    }
                    else
                    {
                        bulkQueryFileInfoCacheResult.Items.Add(new QueryFileInfoCacheResult()
                        {
                            FullPath = nodeFileSyncRequest.FileInfo.FullName,
                            Result = FileInfoCacheResult.None
                        });
                    }
                }
            }
            rsp.SetResult(bulkQueryFileInfoCacheResult);
            _logger.LogInformation(JsonSerializer.Serialize(bulkQueryFileInfoCacheResult));
        }
        catch (Exception ex)
        {
            rsp.ErrorCode = ex.HResult;
            rsp.Message = ex.ToString();
        }
        return rsp;
    }



    [HttpPost("/api/NodeFileSystem/UploadFile")]
    [EnableRateLimiting("UploadFile")]
    [DisableRequestSizeLimit]
    public async Task<ApiResponse<NodeFileSyncRecordModel>> UploadFileAsync()
    {
        var rsp = new ApiResponse<NodeFileSyncRecordModel>();
        try
        {

            var request = HttpContext.Request;

            // validation of Content-Type
            // 1. first, it must be a form-data request
            // 2. a boundary should be found in the Content-Type
            if (!request.HasFormContentType ||
                !MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaTypeHeader) ||
                string.IsNullOrEmpty(mediaTypeHeader.Boundary.Value))
            {
                rsp.SetError(-1, "not supported media type");
                return rsp;
            }

            var boundary = HeaderUtilities.RemoveQuotes(mediaTypeHeader.Boundary.Value).Value;
            var reader = new MultipartReader(boundary, request.Body);
            var section = await reader.ReadNextSectionAsync();
            
            // This sample try to get the first file from request and save it
            // Make changes according to your needs in actual use
            if (section == null)
            {
                rsp.SetError(-1, "file section not found");
            }
            else
            {
                var hasHeader = ContentDispositionHeaderValue.TryParse(
                    section.ContentDisposition,
                    out var contentDisposition);

                if (!hasHeader)
                {
                    rsp.SetError(-1, "content disposition header not found");
                    return rsp;
                }


                if (hasHeader
                    && contentDisposition != null
                    && contentDisposition.DispositionType.Equals("form-data")
                    && !string.IsNullOrEmpty(contentDisposition.FileName.Value))
                {
                    // Don't trust any file name, file extension, and file data from the request unless you trust them completely
                    // Otherwise, it is very likely to cause problems such as virus uploading, disk filling, etc
                    // In short, it is necessary to restrict and verify the upload
                    // Here, we just use the temporary folder and a random file name

                    // Get the temporary folder, and combine a random file name with it

                    NodeFileSyncRequest? nodeFileSyncRequest = null;

                    if (section.Headers == null)
                    {
                        rsp.SetError(-1, "section header not found");
                        return rsp;
                    }

                    if (section.Headers.TryGetValue("NodeFileSyncRequest", out var values))
                    {
                        var str = values.FirstOrDefault();
                        nodeFileSyncRequest = JsonSerializer.Deserialize<NodeFileSyncRequest>(str);
                    }
                    if (nodeFileSyncRequest == null)
                    {
                        rsp.SetError(-1, "node file sync request not found");
                        return rsp;
                    }

                    using var readerStream = new ReaderStream(section.Body, nodeFileSyncRequest.FileInfo.Length);
                    var syncContext = nodeFileSyncRequest.CreateNodeFileUploadContext(readerStream);
                    var ftpClientProcessContext = ActivatorUtilities.CreateInstance<FtpClientProcessContext>(_serviceProvider);
                    await ftpClientProcessContext.ProcessAsync(syncContext);
                    rsp.SetResult(syncContext.Record);

                }
            }

            // If the code runs to this location, it means that no files have been saved

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