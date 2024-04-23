namespace NodeService.WebServer.Controllers
{
    [ApiController]
    [Route("api/[controller]/[action]")]
    public partial class ClientUpdateController : Controller
    {


        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly IMemoryCache _memoryCache;

        public ClientUpdateController(
            IDbContextFactory<ApplicationDbContext> dbContext,
            IMemoryCache memoryCache)
        {
            this._dbContextFactory = dbContext;
            _memoryCache = memoryCache;
        }

        [HttpGet("/api/clientupdate/getupdate")]
        public async Task<ApiResponse<ClientUpdateConfigModel>> GetUpdateAsync([FromQuery] string? name)
        {
            ApiResponse<ClientUpdateConfigModel> apiResponse = new ApiResponse<ClientUpdateConfigModel>();
            try
            {
                using var dbContext = this._dbContextFactory.CreateDbContext();
                if (string.IsNullOrEmpty(name))
                {
                    name = "NodeService.WindowsService";
                }
                var clientUpdateConfig = await
                    dbContext
                    .ClientUpdateConfigurationDbSet
                    .Where(x => x.Status == ClientUpdateStatus.Public)
                    .Where(x => x.Name == name)
                    .OrderByDescending(static x => x.Version)
                    .FirstOrDefaultAsync();

                if (clientUpdateConfig != null && clientUpdateConfig.DnsFilters != null && clientUpdateConfig.DnsFilters.Any())
                {
                    var ipAddress = HttpContext.Connection.RemoteIpAddress.ToString();


                    if (clientUpdateConfig.DnsFilterType == "include")
                    {
                        foreach (var filter in clientUpdateConfig.DnsFilters)
                        {
                            var nodeInfo = await dbContext.NodeInfoDbSet.FirstOrDefaultAsync(x => x.Name == filter.Value);
                            if (nodeInfo == null)
                            {
                                continue;
                            }
                            if (nodeInfo.Profile.IpAddress == ipAddress)
                            {
                                apiResponse.Result = clientUpdateConfig;
                                break;
                            }
                        }
                    }
                    else if (clientUpdateConfig.DnsFilterType == "exclude")
                    {
                        foreach (var filter in clientUpdateConfig.DnsFilters)
                        {
                            var nodeInfo = await dbContext.NodeInfoDbSet.FirstOrDefaultAsync(x => x.Name == filter.Value);
                            if (nodeInfo == null)
                            {
                                continue;
                            }
                            if (nodeInfo.Profile.IpAddress == ipAddress)
                            {
                                apiResponse.Result = null;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    apiResponse.Result = clientUpdateConfig;
                }
            }
            catch (Exception ex)
            {
                apiResponse.ErrorCode = ex.HResult;
                apiResponse.Message = ex.Message;
            }
            return apiResponse;
        }

        [HttpPost("/api/clientupdate/addorupdate")]
        public async Task<ApiResponse> AddOrUpdateAsync([FromBody] ClientUpdateConfigModel model)
        {
            ApiResponse apiResponse = new ApiResponse();
            try
            {
                using var dbContext = this._dbContextFactory.CreateDbContext();
                var modelFromDb = await dbContext.ClientUpdateConfigurationDbSet.FindAsync(model.Id);
                if (modelFromDb == null)
                {
                    model.PackageConfig = null;
                    await dbContext.ClientUpdateConfigurationDbSet.AddAsync(model);
                }
                else
                {
                    modelFromDb.Id = model.Id;
                    modelFromDb.Name = model.Name;
                    modelFromDb.Version = model.Version;
                    modelFromDb.DecompressionMethod = model.DecompressionMethod;
                    modelFromDb.DnsFilters = model.DnsFilters;
                    modelFromDb.DnsFilterType = model.DnsFilterType;
                    modelFromDb.PackageConfigId = model.PackageConfigId;
                    modelFromDb.Status = model.Status;
                }
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                apiResponse.ErrorCode = ex.HResult;
                apiResponse.Message = ex.Message;
            }
            return apiResponse;
        }

        [HttpGet("/api/clientupdate/list")]
        public async Task<PaginationResponse<ClientUpdateConfigModel>> QueryClientUpdateListAsync()
        {
            PaginationResponse<ClientUpdateConfigModel> apiResponse = new PaginationResponse<ClientUpdateConfigModel>();
            try
            {
                using var dbContext = this._dbContextFactory.CreateDbContext();
                apiResponse.TotalCount = await dbContext.ClientUpdateConfigurationDbSet.CountAsync();
                apiResponse.Result = await dbContext.ClientUpdateConfigurationDbSet.OrderByDescending(x => x.Version).ToListAsync();
            }
            catch (Exception ex)
            {
                apiResponse.ErrorCode = ex.HResult;
                apiResponse.Message = ex.Message;
            }
            return apiResponse;
        }

        [HttpPost("/api/clientupdate/remove")]
        public async Task<ApiResponse> RemoveAsync([FromBody] ClientUpdateConfigModel model)
        {
            ApiResponse apiResponse = new ApiResponse();
            try
            {
                using var dbContext = this._dbContextFactory.CreateDbContext();
                dbContext.ClientUpdateConfigurationDbSet.Remove(model);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                apiResponse.ErrorCode = ex.HResult;
                apiResponse.Message = ex.Message;
            }
            return apiResponse;
        }


    }
}
