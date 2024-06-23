using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Data;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.QueryOptimize;

namespace NodeService.WebServer.Controllers;

public partial class DataQualityController
{
    [HttpGet("/api/DataQuality/FileRecords/List")]
    public async Task<PaginationResponse<FileRecordModel>> QueryNodeFileListAsync(
        [FromQuery] QueryFileRecordListParameters queryParameters,
        CancellationToken cancellationToken = default)
    {
        var apiResponse = new PaginationResponse<FileRecordModel>();
        try
        {
            var fileRecordQueryOperation =
                new BatchQueueOperation<FileRecordBatchQueryParameters, ListQueryResult<FileRecordModel>>(
                    new FileRecordBatchQueryParameters(queryParameters,
                        new PaginationInfo(queryParameters.PageIndex, queryParameters.PageSize)
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

    [HttpGet("/api/DataQuality/FileRecords/Categories/List")]
    public async Task<PaginationResponse<string>> QueryCategoriesListAsync(
        [FromQuery] PaginationQueryParameters queryParameters,
        CancellationToken cancellationToken = default)
    {
        var apiResponse = new PaginationResponse<string>();
        try
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var startIndex = (queryParameters.PageIndex - 1) * queryParameters.PageSize;
            ArgumentOutOfRangeException.ThrowIfLessThan(startIndex, 0, nameof(queryParameters.PageIndex));
            var queryable = dbContext.FileRecordDbSet
                .GroupBy(x => x.Category)
                .Select(x => x.Key);

            var categories = await queryable.Skip(startIndex)
                .Take(queryParameters.PageSize).ToArrayAsync(cancellationToken);
            apiResponse.TotalCount = await queryable.CountAsync(cancellationToken);
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

    [HttpPost("/api/DataQuality/FileRecords/AddOrUpdate")]
    public async Task<ApiResponse> AddOrUpdateAsync(
        [FromBody] FileRecordModel model,
        CancellationToken cancellationToken = default)
    {
        var apiResponse = new ApiResponse();
        try
        {
            var fileRecordOperation =
                new BatchQueueOperation<FileRecordModel, bool>(model, BatchQueueOperationKind.AddOrUpdate);
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

    [HttpPost("/api/DataQuality/FileRecords/Remove")]
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