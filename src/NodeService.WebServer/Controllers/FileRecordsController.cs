using Microsoft.AspNetCore.RateLimiting;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Data;
using NodeService.WebServer.Data.Repositories.Specifications;
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
    readonly BatchQueue<BatchQueueOperation<FileRecordSpecification, ListQueryResult<FileRecordModel>>> _queryOpBatchQueue;
    readonly BatchQueue<BatchQueueOperation<FileRecordModel, bool>> _insertUpdateDeleteOpBatchQueue;
    readonly IMemoryCache _memoryCache;

    public FileRecordsController(
        ILogger<FileRecordsController> logger,
        ExceptionCounter exceptionCounter,
        [FromKeyedServices(nameof(FileRecordQueryService))]
        BatchQueue<BatchQueueOperation<FileRecordSpecification, ListQueryResult<FileRecordModel>>> queryOpBatchQueue,
        [FromKeyedServices(nameof(FileRecordInsertUpdateDeleteService))]
        BatchQueue<BatchQueueOperation<FileRecordModel, bool>> insertUpdateDeleteOpBatchQueue,
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        IMemoryCache  memoryCache)
    {
        _dbContextFactory = dbContextFactory;
        _exceptionCounter = exceptionCounter;
        _memoryCache = memoryCache;
        _logger = logger;
        _queryOpBatchQueue = queryOpBatchQueue;
        _insertUpdateDeleteOpBatchQueue = insertUpdateDeleteOpBatchQueue;
    }

    [HttpGet("/api/filerecords/{nodeId}/list")]
    public async Task<PaginationResponse<FileRecordModel>> QueryNodeFileListAsync(
        string nodeId,
        [FromQuery] PaginationQueryParameters queryParameters,
        CancellationToken cancellationToken = default)
    {
        var apiResponse = new PaginationResponse<FileRecordModel>();
        try
        {
            var fileRecordQueryOperation = new BatchQueueOperation<FileRecordSpecification, ListQueryResult<FileRecordModel>>(
                new FileRecordSpecification(nodeId, queryParameters.Keywords, queryParameters.SortDescriptions),
                BatchQueueOperationKind.Query);
            await _queryOpBatchQueue.SendAsync(fileRecordQueryOperation, cancellationToken);
            var queryResult = await fileRecordQueryOperation.WaitAsync(cancellationToken);
            apiResponse.SetResult(queryResult);
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
            var fileRecordOperation = new BatchQueueOperation<FileRecordModel, bool>(model, BatchQueueOperationKind.InsertOrUpdate);
            await _insertUpdateDeleteOpBatchQueue.SendAsync(fileRecordOperation, cancellationToken);
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
    public async Task<ApiResponse> RemoveAsync(
        [FromBody] FileRecordModel model,
        CancellationToken cancellationToken = default)
    {
        var apiResponse = new ApiResponse();

        try
        {
            var fileRecordOperation = new BatchQueueOperation<FileRecordModel, bool>(model, BatchQueueOperationKind.Delete);
            await _insertUpdateDeleteOpBatchQueue.SendAsync(fileRecordOperation, cancellationToken);
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
}