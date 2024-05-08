using Microsoft.AspNetCore.RateLimiting;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Messages;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.FileRecords;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public class FileRecordsController : Controller
{
    readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    readonly ExceptionCounter _exceptionCounter;
    readonly ILogger<FileRecordsController> _logger;
    readonly BatchQueue<BatchQueueOperation<object, object>> _fileRecordBatchQueue;
    readonly IMemoryCache _memoryCache;

    public FileRecordsController(
        ILogger<FileRecordsController> logger,
        ExceptionCounter exceptionCounter,
        [FromKeyedServices(nameof(FileRecordService))]BatchQueue<BatchQueueOperation<object, object>> fileRecordBatchQueue,
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        IMemoryCache  memoryCache)
    {
        _dbContextFactory = dbContextFactory;
        _exceptionCounter = exceptionCounter;
        _memoryCache = memoryCache;
        _logger = logger;
        _fileRecordBatchQueue = fileRecordBatchQueue;
    }

    [HttpGet("/api/filerecords/{nodeId}/list")]
    public async Task<PaginationResponse<FileRecordModel>> QueryNodeFileListAsync(
        string nodeId,
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        var apiResponse = new PaginationResponse<FileRecordModel>();
        try
        {
            var fileRecordOperation = new BatchQueueOperation<object, object>(
                Tuple.Create(nodeId, queryParameters),
                BatchQueueOperationKind.Query);
            await _fileRecordBatchQueue.SendAsync(fileRecordOperation);
            PaginationResponse<FileRecordModel>? value = await fileRecordOperation.WaitAsync() as PaginationResponse<FileRecordModel>;
            if (value != null)
            {
                apiResponse = value;
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.ToString();
        }

        return apiResponse;
    }

    [HttpPost("/api/filerecords/{nodeId}/addorupdate")]
    public async Task<ApiResponse> AddOrUpdateAsync(
        [FromBody] FileRecordModel model,
        CancellationToken cancellationToken = default)
    {
        var apiResponse = new ApiResponse();
        try
        {
            var fileRecordOperation = new BatchQueueOperation<object, object>(model, BatchQueueOperationKind.InsertOrUpdate);
            await _fileRecordBatchQueue.SendAsync(fileRecordOperation, cancellationToken);
            await fileRecordOperation.WaitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.ToString();
        }

        return apiResponse;
    }

    [HttpPost("/api/filerecords/{nodeId}/remove")]
    public async Task<PaginationResponse<FileRecordModel>> RemoveAsync([FromBody] FileRecordModel fileRecord)
    {
        var apiResponse = new PaginationResponse<FileRecordModel>();
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        try
        {
            dbContext.Remove(fileRecord);
            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.ToString();
        }

        return apiResponse;
    }
}