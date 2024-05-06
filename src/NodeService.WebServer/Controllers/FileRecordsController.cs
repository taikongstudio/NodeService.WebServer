using Microsoft.AspNetCore.RateLimiting;
using NodeService.WebServer.Models;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public class FileRecordsController : Controller
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ExceptionCounter _exceptionCounter;
    private readonly ILogger<FileRecordsController> _logger;
    private readonly IMemoryCache _memoryCache;

    public FileRecordsController(
        ILogger<FileRecordsController> logger,
        ExceptionCounter exceptionCounter,
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        IMemoryCache  memoryCache)
    {
        _dbContextFactory = dbContextFactory;
        _exceptionCounter = exceptionCounter;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    [EnableRateLimiting("Concurrency")]
    [HttpGet("/api/filerecords/{nodeId}/list")]
    public async Task<PaginationResponse<FileRecordModel>> QueryNodeFileListAsync(
        string nodeId,
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        var apiResponse = new PaginationResponse<FileRecordModel>();
        try
        {
            apiResponse = await QueryInternal(nodeId, queryParameters);
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

    private async Task<PaginationResponse<FileRecordModel>> QueryInternal(string nodeId, PaginationQueryParameters queryParameters)
    {
        var apiResponse = new PaginationResponse<FileRecordModel>();
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        if (string.IsNullOrEmpty(queryParameters.Keywords))
        {
            var key = $"{nameof(FileRecordModel)}:{nodeId}";

            IQueryable<FileRecordModel> queryable = dbContext.FileRecordsDbSet.Where(x => x.Id == nodeId)
                .OrderBy(x => x.ModifyDateTime);

            apiResponse = await queryable.QueryPageItemsAsync(queryParameters);
        }
        else
        {
            apiResponse.SetResult([await dbContext.FileRecordsDbSet.FindAsync(nodeId, queryParameters.Keywords)]);
            apiResponse.SetTotalCount(1);
            apiResponse.SetPageIndex(queryParameters.PageIndex);
            apiResponse.SetPageSize(queryParameters.PageSize);

        }
        return apiResponse;
    }

    [HttpPost("/api/filerecords/{nodeId}/addorupdate")]
    public async Task<ApiResponse> AddOrUpdateAsync([FromBody] FileRecordModel model)
    {
        var apiResponse = new ApiResponse();
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        try
        {
            var fileRecordFromDb = await dbContext.FileRecordsDbSet.FindAsync(model.Id, model.Name);
            if (fileRecordFromDb == null)
            {
                await dbContext.FileRecordsDbSet.AddAsync(model);
            }
            else
            {
                fileRecordFromDb.ModifyDateTime = model.ModifyDateTime;
                if (model.Properties != null) fileRecordFromDb.Properties = model.Properties;
                if (model.FileHashValue != null) fileRecordFromDb.FileHashValue = model.FileHashValue;
                if (model.Size > 0) fileRecordFromDb.Size = model.Size;
                if (model.OriginalFileName != null) fileRecordFromDb.OriginalFileName = model.OriginalFileName;
                if (model.State != FileRecordState.None) fileRecordFromDb.State = model.State;
                if (model.CompressedSize > 0) fileRecordFromDb.CompressedSize = model.CompressedSize;
                if (model.CompressedFileHashValue != null)
                    fileRecordFromDb.CompressedFileHashValue = model.CompressedFileHashValue;
            }

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