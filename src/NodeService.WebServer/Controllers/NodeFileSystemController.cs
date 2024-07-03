using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.NodeFileSystem;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.NodeFileSystem;
using System.Net;
using System.Reactive.Disposables;
using System.Threading;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public class NodeFileSystemController : Controller
{
    private readonly ILogger<NodeFileSystemController> _logger;
    private readonly ExceptionCounter _exceptionCounter;
    private readonly BatchQueue<BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncResponse>> _nodeFileSyncBatchQueue;

    public NodeFileSystemController(
        ILogger<NodeFileSystemController> logger,
        ExceptionCounter exceptionCounter,
        BatchQueue<BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncResponse>> fileSyncBatchQueue
    )
    {
        _logger = logger;
        _exceptionCounter = exceptionCounter;
        _nodeFileSyncBatchQueue = fileSyncBatchQueue;
    }

    [HttpGet("/api/NodeFileSystem/QueueStatus")]
    public Task<ApiResponse<NodeFileSyncQueueStatus>> QueryQueueStatus()
    {
        var rsp = new ApiResponse<NodeFileSyncQueueStatus>();
        rsp.SetResult(new NodeFileSyncQueueStatus()
        {
            QueuedCount = _nodeFileSyncBatchQueue.AvailableCount
        });
        return Task.FromResult(rsp);
    }

    [HttpPost("/api/NodeFileSystem/UploadFile")]
    [DisableRequestSizeLimit]
    public async Task<ApiResponse<NodeFileSyncResponse>> UploadFileAsync()
    {
        var rsp = new ApiResponse<NodeFileSyncResponse>();
        NodeFileSyncResponse? nodeFileSyncResponse = null;
        try
        {
            if (_nodeFileSyncBatchQueue.AvailableCount > 100)
            {
                throw new InvalidOperationException("queue limit");
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

                if (hasContentDispositionHeader)

                    if (hasContentDispositionHeader && contentDisposition.DispositionType.Equals("form-data") &&
                        !string.IsNullOrEmpty(contentDisposition.FileName.Value))
                    {
                        // Don't trust any file name, file extension, and file data from the request unless you trust them completely
                        // Otherwise, it is very likely to cause problems such as virus uploading, disk filling, etc
                        // In short, it is necessary to restrict and verify the upload
                        // Here, we just use the temporary folder and a random file name

                        // Get the temporary folder, and combine a random file name with it

                        NodeFileSyncRequest? nodeFileSyncRequest = null;
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

                        //const string TempUploadedDirectory = "../NodeFileSystem/Uploaded";
                        //if (!Directory.Exists(TempUploadedDirectory))
                        //{
                        //    Directory.CreateDirectory(TempUploadedDirectory);
                        //}
                        //var tempFilePath = Path.Combine(TempUploadedDirectory, Guid.NewGuid().ToString() + ".tmp");
                        //var tempStream = System.IO.File.Create(tempFilePath);
                        //using var disposeable = Disposable.Create(() =>
                        //{
                        //    tempStream.Close();
                        //    System.IO.File.Delete(tempFilePath);
                        //});
                        //await section.Body.CopyToAsync(tempStream);
                        //tempStream.Seek(0, SeekOrigin.Begin);
                        Stopwatch stopwatch = Stopwatch.StartNew();
                        nodeFileSyncRequest = nodeFileSyncRequest with { Stream = section.Body };
                        using var nodeFileUploadContext = await nodeFileSyncRequest.CreateNodeFileUploadContextAsync(
                            new DefaultSHA256HashAlgorithmProvider(),
                            new DefaultGzipCompressionProvider());
                        nodeFileSyncRequest = nodeFileSyncRequest with { Stream = nodeFileUploadContext.Stream };
                        var op = new BatchQueueOperation<NodeFileSyncRequest, NodeFileSyncResponse>(
                             nodeFileSyncRequest,
                             BatchQueueOperationKind.AddOrUpdate,
                             BatchQueueOperationPriority.Normal);
                        op.Context = nodeFileUploadContext;
                        await _nodeFileSyncBatchQueue.SendAsync(op);
                        nodeFileSyncResponse = await op.WaitAsync();
                        stopwatch.Stop();
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

        rsp.SetResult(nodeFileSyncResponse);
        return rsp;
    }



}