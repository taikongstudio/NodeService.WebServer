using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Data;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.FileRecords;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public class FileRecordsController : Controller
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ExceptionCounter _exceptionCounter;
    private readonly BatchQueue<BatchQueueOperation<FileRecordModel, bool>> _insertUpdateDeleteOpBatchQueue;
    private readonly ILogger<FileRecordsController> _logger;
    private readonly IMemoryCache _memoryCache;

    private readonly BatchQueue<BatchQueueOperation<(FileRecordSpecification Specification, PaginationInfo PaginationInfo), ListQueryResult<FileRecordModel>>>
        _queryOpBatchQueue;

    public FileRecordsController(
        ILogger<FileRecordsController> logger,
        ExceptionCounter exceptionCounter,
        [FromKeyedServices(nameof(FileRecordQueryService))]
        BatchQueue<BatchQueueOperation<(FileRecordSpecification Specification, PaginationInfo PaginationInfo), ListQueryResult<FileRecordModel>>> queryOpBatchQueue,
        [FromKeyedServices(nameof(FileRecordInsertUpdateDeleteService))]
        BatchQueue<BatchQueueOperation<FileRecordModel, bool>> insertUpdateDeleteOpBatchQueue,
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        IMemoryCache memoryCache)
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
        [FromQuery] QueryFileRecordListParameters queryParameters,
        CancellationToken cancellationToken = default)
    {
        var apiResponse = new PaginationResponse<FileRecordModel>();
        try
        {
            var fileRecordQueryOperation =
                new BatchQueueOperation<(FileRecordSpecification Specification, PaginationInfo PaginationInfo), ListQueryResult<FileRecordModel>>(
                    (
                    new FileRecordSpecification(nodeId,
                        queryParameters.Category,
                        queryParameters.Keywords,
                        queryParameters.SortDescriptions), 
                    new PaginationInfo(
                        queryParameters.PageIndex,
                        queryParameters.PageSize)
                    ),
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

    [HttpGet("/api/filerecords/list")]
    public async Task<PaginationResponse<FileRecordModel>> QueryNodeFileListAsync(
    [FromQuery] QueryFileRecordListParameters queryParameters,
    CancellationToken cancellationToken = default)
    {
        var apiResponse = new PaginationResponse<FileRecordModel>();
        try
        {
            var fileRecordQueryOperation =
                new BatchQueueOperation<(FileRecordSpecification Specification, PaginationInfo PaginationInfo), ListQueryResult<FileRecordModel>>(
                    (new FileRecordSpecification(
                       queryParameters.Category,
                       queryParameters.State,
                       DataFilterCollection<string>.Includes(queryParameters.NodeIdList),
                       DataFilterCollection<string>.Includes(string.IsNullOrEmpty(queryParameters.Keywords) ? [] : [queryParameters.Keywords]),
                       queryParameters.SortDescriptions),
                    new PaginationInfo(queryParameters.PageIndex, queryParameters.PageSize)
                    ),
                    BatchQueueOperationKind.Query);
            await _queryOpBatchQueue.SendAsync(fileRecordQueryOperation, cancellationToken);
            var queryResult = await fileRecordQueryOperation.WaitAsync(cancellationToken);
            queryResult = new ListQueryResult<FileRecordModel>(10, 10, 1, [new FileRecordModel()]);
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

    [HttpGet("/api/filerecords/categories")]
    public async Task<PaginationResponse<string>> QueryCategoriesListAsync(
        [FromQuery] PaginationQueryParameters queryParameters,
        CancellationToken cancellationToken = default)
    {
        var apiResponse = new PaginationResponse<string>();
        try
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            int startIndex = (queryParameters.PageIndex - 1) * queryParameters.PageSize;
            ArgumentOutOfRangeException.ThrowIfLessThan(startIndex, 0, nameof(queryParameters.PageIndex));
            var queryable = dbContext.FileRecordsDbSet
                .GroupBy(x => x.Category)
                .Select(x => x.Key);

            var categories = await queryable.Skip(startIndex)
                .Take(queryParameters.PageSize).ToArrayAsync();
            apiResponse.TotalCount = await queryable.CountAsync();
            apiResponse.PageSize = queryParameters.PageIndex;
            apiResponse.PageIndex = queryParameters.PageSize;
            apiResponse.SetResult(categories);
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
            var fileRecordOperation =
                new BatchQueueOperation<FileRecordModel, bool>(model, BatchQueueOperationKind.InsertOrUpdate);
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
            var fileRecordOperation =
                new BatchQueueOperation<FileRecordModel, bool>(model, BatchQueueOperationKind.Delete);
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