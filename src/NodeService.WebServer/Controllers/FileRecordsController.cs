namespace NodeService.WebServer.Controllers
{
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

        [HttpGet("/api/filerecords/{nodeId}/list")]
        public async Task<PaginationResponse<FileRecordModel>> QueryNodeFileListAsync(
            string nodeId,
            [FromQuery] int? pageIndex = null,
            [FromQuery] int? pageSize = null,
            [FromQuery] string? fullName = null)
        {
            PaginationResponse<FileRecordModel> apiResponse = new PaginationResponse<FileRecordModel>();
            using var dbContext = _dbContextFactory.CreateDbContext();
            try
            {
                if (pageIndex == null)
                {
                    pageIndex = 0;
                }
                if (pageSize == null)
                {
                    pageSize = 500;
                }
                var queryable = dbContext.FileRecordsDbSet.AsQueryable();
                if (fullName == null)
                {
                    var totalCount = await dbContext.FileRecordsDbSet.AsQueryable()
                        .Where(x => x.Id == nodeId)
                        .OrderBy(x => x.ModifyDateTime).CountAsync();
                    apiResponse.Result = await dbContext.FileRecordsDbSet.AsQueryable()
                        .Where(x => x.Id == nodeId)
                        .OrderBy(x => x.ModifyDateTime)
                        .Skip(pageSize.Value * pageIndex.Value)
                        .Take(pageSize.Value)
                        .ToArrayAsync();
                    apiResponse.PageIndex = pageIndex.Value;
                    apiResponse.PageSize = pageSize.Value;
                    apiResponse.TotalCount = totalCount;


                }
                else
                {
                    apiResponse.Result = [await dbContext.FileRecordsDbSet.FindAsync(nodeId, fullName)];
                }

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
        public async Task<ApiResponse> AddOrUpdateAsync([FromBody] FileRecordModel fileRecord)
        {
            ApiResponse apiResponse = new ApiResponse();
            using var dbContext = _dbContextFactory.CreateDbContext();
            try
            {
                var fileRecordFromDb = await dbContext.FileRecordsDbSet.FindAsync(fileRecord.Id, fileRecord.Name);
                if (fileRecordFromDb == null)
                {
                    await dbContext.FileRecordsDbSet.AddAsync(fileRecord);
                }
                else
                {
                    fileRecordFromDb.ModifyDateTime = fileRecord.ModifyDateTime;
                    fileRecordFromDb.Properties = fileRecord.Properties;
                    fileRecordFromDb.FileHashValue = fileRecord.FileHashValue;
                    fileRecordFromDb.Size = fileRecord.Size;
                    fileRecordFromDb.OriginalFileName = fileRecord.OriginalFileName;
                    fileRecordFromDb.CompressedSize = fileRecord.CompressedSize;
                    fileRecordFromDb.State = fileRecord.State;
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
            PaginationResponse<FileRecordModel> apiResponse = new PaginationResponse<FileRecordModel>();
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
}
