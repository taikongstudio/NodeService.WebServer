using Microsoft.AspNetCore.RateLimiting;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public class FileRecordsController : Controller
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ILogger<FileRecordsController> _logger;

    public FileRecordsController(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        ILogger<FileRecordsController> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    [EnableRateLimiting("Concurrency")]
    [HttpGet("/api/filerecords/{nodeId}/list")]
    public async Task<PaginationResponse<FileRecordModel>> QueryNodeFileListAsync(
        string nodeId,
        [FromQuery] int? pageIndex = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? fullName = null)
    {
        var apiResponse = new PaginationResponse<FileRecordModel>();
        try
        {
            using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            if (pageIndex == null) pageIndex = 0;
            if (pageSize == null) pageSize = 500;
            IQueryable<FileRecordModel> queryable = dbContext.FileRecordsDbSet.Where(x => x.Id == nodeId)
                    .OrderBy(x => x.ModifyDateTime);
            if (string.IsNullOrEmpty(fullName))
            {
                var totalCount = await queryable.CountAsync();
                apiResponse.Result = await queryable
                                        .Skip(pageSize.Value * pageIndex.Value)
                                        .Take(pageSize.Value)
                                        .ToArrayAsync();
                apiResponse.TotalCount = totalCount;
            }
            else
            {
                apiResponse.Result = [await dbContext.FileRecordsDbSet.FindAsync(nodeId, fullName)];
                apiResponse.TotalCount = apiResponse.Result == null ? 0 : 1;
            }
            apiResponse.PageIndex = pageIndex.Value;
            apiResponse.PageSize = pageSize.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.ToString();
        }

        return apiResponse;
    }

    [HttpPost("/api/filerecords/{nodeId}/addorupdate")]
    public async Task<ApiResponse> AddOrUpdateAsync([FromBody] FileRecordModel model)
    {
        var apiResponse = new ApiResponse();
        using var dbContext = _dbContextFactory.CreateDbContext();
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
        using var dbContext = _dbContextFactory.CreateDbContext();
        try
        {
            dbContext.Remove(fileRecord);
            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.ToString();
        }

        return apiResponse;
    }
}