using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Net.Http.Headers;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.NodeFileSystem;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.NodeFileSystem;
using System.IO.Pipelines;
using System.Security.Cryptography;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public class NodeFileSystemController : Controller
{
    readonly ILogger<NodeFileSystemController> _logger;
    readonly ExceptionCounter _exceptionCounter;
    readonly IServiceProvider _serviceProvider;
    readonly NodeBatchProcessQueueDictionary _batchProcessQueueDictionary;
    readonly IDistributedCache _distributedCache;
    readonly BatchQueue<BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncRecordModel, NodeFileUploadContext>> _nodeFileSyncBatchQueue;
    readonly BatchQueue<BatchQueueOperation<NodeFileSystemInfoIndexServiceParameters, NodeFileSystemInfoIndexServiceResult>> _queryQueue;
    readonly BatchQueue<BatchQueueOperation<NodeFileSystemSyncRecordServiceParameters, NodeFileSystemSyncRecordServiceResult>> _syncRecordQueryQueue;

    public NodeFileSystemController(
        ILogger<NodeFileSystemController> logger,
        IServiceProvider serviceProvider,
        ExceptionCounter exceptionCounter,
        NodeBatchProcessQueueDictionary batchProcessQueueDictionary,
        BatchQueue<BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncRecordModel, NodeFileUploadContext>> nodeFileSyncBatchQueue,
        BatchQueue<BatchQueueOperation<NodeFileSystemInfoIndexServiceParameters, NodeFileSystemInfoIndexServiceResult>> queryQueue,
        BatchQueue<BatchQueueOperation<NodeFileSystemSyncRecordServiceParameters, NodeFileSystemSyncRecordServiceResult>> syncRecordQueryQueue
    )
    {
        _logger = logger;
        _exceptionCounter = exceptionCounter;
        _serviceProvider = serviceProvider;
        _batchProcessQueueDictionary = batchProcessQueueDictionary;
        _nodeFileSyncBatchQueue = nodeFileSyncBatchQueue;
        _queryQueue = queryQueue;
        _syncRecordQueryQueue = syncRecordQueryQueue;
    }

    [HttpGet("/api/NodeFileSystem/QueueStatus")]
    public Task<ApiResponse<NodeFileSyncQueueStatus>> QueryQueueStatus()
    {
        var rsp = new ApiResponse<NodeFileSyncQueueStatus>();
        rsp.SetResult(new NodeFileSyncQueueStatus()
        {
            SyncRequestAvaibleCount = _nodeFileSyncBatchQueue.AvailableCount,
            BatchProcessQueueCount = _batchProcessQueueDictionary.Count,
            BatchProcessQueues = _batchProcessQueueDictionary.Select(x => new BatchProcessQueueInfo()
            {
                QueueId = x.Key,
                QueueName = x.Value.QueueName,
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
    public async Task<PaginationResponse<NodeFileSyncRecordModel>> GetSyncRecordListAsync(
        [FromQuery] QueryNodeFileSystemSyncRecordParameters parameters,
        CancellationToken cancellationToken = default)
    {
        var rsp = new PaginationResponse<NodeFileSyncRecordModel>();
        try
        {
            var queryParameter = new NodeFileSystemSyncRecordQueryParameters(parameters);
            var serviceParameter = new NodeFileSystemSyncRecordServiceParameters(queryParameter);
            var op = new BatchQueueOperation<NodeFileSystemSyncRecordServiceParameters, NodeFileSystemSyncRecordServiceResult>
                (serviceParameter, BatchQueueOperationKind.Query);
            await _syncRecordQueryQueue.SendAsync(op, cancellationToken);
            var result = await op.WaitAsync(cancellationToken);
            var listQueryResult = result.Result.AsT1.Result;
            rsp.SetResult(listQueryResult);
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

    [HttpGet("/api/NodeFileSystem/GetObjectInfo")]
    public async Task<PaginationResponse<NodeFileSystemInfoModel>> GetObjectInfoAsync(
        [FromQuery] QueryNodeFileSystemInfoParameters queryParameters,
        CancellationToken cancellationToken = default)
    {
        var rsp = new PaginationResponse<NodeFileSystemInfoModel>();
        try
        {
            if (queryParameters.NodeInfoId == null)
            {
                rsp.SetError(-1, "invalid node info id");
                return rsp;
            }
            if (queryParameters.FilePathList != null && queryParameters.FilePathList.Count > 0)
            {
                List<NodeFileSystemInfoModel> list = [];
                List<string> notFoundFilePathList = [];
                foreach (var filePath in queryParameters.FilePathList)
                {
                    var nodeFilePath = NodeFileSystemHelper.GetNodeFilePath(queryParameters.NodeInfoId, filePath);
                    var jsonString = _distributedCache.GetString(nodeFilePath);
                    if (jsonString == null)
                    {
                        var nodeFilePathHash = NodeFileSystemHelper.GetNodeFilePathHash(nodeFilePath);
                        notFoundFilePathList.Add(filePath);
                    }
                    else
                    {
                        list.Add(JsonSerializer.Deserialize<NodeFileSystemInfoModel>(jsonString));
                    }
                }
                var notFoundFileParameters = queryParameters with
                {
                    FilePathList = notFoundFilePathList,
                };
                var op = new BatchQueueOperation<NodeFileSystemInfoIndexServiceParameters, NodeFileSystemInfoIndexServiceResult>(
                    new NodeFileSystemInfoIndexServiceParameters(new NodeFileSystemInfoQueryParameters(notFoundFileParameters)),
                    BatchQueueOperationKind.Query);
                await _queryQueue.SendAsync(op, cancellationToken);
                var serviceResult = await op.WaitAsync(cancellationToken);
                var listQueryResult = serviceResult.Result.AsT0;
                list.AddRange(listQueryResult.Items);
                rsp.SetResult(new ListQueryResult<NodeFileSystemInfoModel>(list.Count, 1, list.Count, list));
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }

        return rsp;
    }

    [HttpGet("/api/NodeFileSystem/DeleteHittestResultCache")]
    public async Task<ApiResponse<int>> DeleteHittestResultCacheAsync()
    {
        var rsp = new ApiResponse<int>();
        try
        {
            var count = await NodeFileSystemHelper.DeleteHittestResultCacheAsync(_serviceProvider);
            rsp.SetResult(count);
        }
        catch (Exception ex)
        {
            rsp.ErrorCode = ex.HResult;
            rsp.Message = ex.ToString();
        }
        return rsp;
    }

    [HttpPost("/api/NodeFileSystem/Hittest")]
    public async Task<ApiResponse<NodeFileHittestResult>> HittestAsync(NodeFileSyncRequest nodeFileSyncRequest)
    {
        var rsp = new ApiResponse<NodeFileHittestResult>();
        try
        {
            var hittestResult = await NodeFileSystemHelper.HittestAsync(
                _serviceProvider,
                nodeFileSyncRequest.NodeInfoId,
                nodeFileSyncRequest.FileInfo);
            if (hittestResult)
            {
                rsp.SetResult(NodeFileHittestResult.Hittested);
            }
            else
            {
                rsp.SetResult(NodeFileHittestResult.None);
            }
        }
        catch (Exception ex)
        {
            rsp.ErrorCode = ex.HResult;
            rsp.Message = ex.ToString();
        }
        return rsp;
    }

    [HttpPost("/api/NodeFileSystem/UploadFile")]
    [DisableRequestSizeLimit]
    public async Task<ApiResponse<NodeFileSyncRecordModel>> UploadFileAsync()
    {
        var rsp = new ApiResponse<NodeFileSyncRecordModel>();
        try
        {
            if (_nodeFileSyncBatchQueue.AvailableCount > 100)
            {
                rsp.SetError(-1, "reach queue limit");
                return rsp;
            }

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
                var hasContentDispositionHeader = ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var contentDisposition);

                if (!hasContentDispositionHeader)
                {
                    rsp.SetError(-1, "content disposition header not found");
                    return rsp;
                }


                if (hasContentDispositionHeader && contentDisposition != null && contentDisposition.DispositionType.Equals("form-data") &&
                    !string.IsNullOrEmpty(contentDisposition.FileName.Value))
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
                        rsp.SetError(-1, "NodeFileSyncRequest not found");
                        return rsp;
                    }

                    var hittestResult = await NodeFileSystemHelper.HittestAsync(
                        _serviceProvider,
                        nodeFileSyncRequest.NodeInfoId,
                        nodeFileSyncRequest.FileInfo);
                    if (hittestResult)
                    {
                        rsp.SetError(-1, "found");
                    }
                    else
                    {

                        var stopwatch = Stopwatch.StartNew();
                        using var readerStream = new PipeReaderStream(section.Body, nodeFileSyncRequest.FileInfo.Length);
                        var uploadContext = nodeFileSyncRequest.CreateNodeFileUploadContext(readerStream);
                        var op = new BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncRecordModel, NodeFileUploadContext>(
                             nodeFileSyncRequest,
                             BatchQueueOperationKind.AddOrUpdate,
                             BatchQueueOperationPriority.Normal)
                        {
                            Context = uploadContext
                        };
                        await _nodeFileSyncBatchQueue.SendAsync(op);
                        var syncRecord = await op.WaitAsync();
                        rsp.SetResult(uploadContext.SyncRecord);
                        if (syncRecord.Status == NodeFileSyncStatus.Queued)
                        {
                            try
                            {
                                var status = await uploadContext.WaitAsync(default);
                            }
                            catch (Exception ex)
                            {
                                _exceptionCounter.AddOrUpdate(ex, nodeFileSyncRequest.NodeInfoId);
                                _logger.LogError(ex.ToString());
                            }
                            finally
                            {

                            }

                        }
                        stopwatch.Stop();
                    }

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