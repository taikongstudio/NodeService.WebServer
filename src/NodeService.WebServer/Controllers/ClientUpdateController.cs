using Microsoft.AspNetCore.RateLimiting;
using NodeService.Infrastructure.Models;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public partial class ClientUpdateController : Controller
{
    private readonly ILogger<ClientUpdateController> _logger;
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly IMemoryCache _memoryCache;
    private readonly ExceptionCounter _exceptionCounter;

    public ClientUpdateController(
        ILogger<ClientUpdateController> logger,
        ExceptionCounter exceptionCounter,
        IDbContextFactory<ApplicationDbContext> dbContext,
        IMemoryCache memoryCache)
    {
        _logger = logger;
        _dbContextFactory = dbContext;
        _memoryCache = memoryCache;
        _exceptionCounter = exceptionCounter;
    }

    [HttpGet("/api/clientupdate/getupdate")]
    public async Task<ApiResponse<ClientUpdateConfigModel>> GetUpdateAsync([FromQuery] string? name)
    {
        var apiResponse = new ApiResponse<ClientUpdateConfigModel>();
        try
        {
            ClientUpdateConfigModel? clientUpdateConfig = null;
            if (string.IsNullOrEmpty(name)) return apiResponse;
            var key = $"ClientUpdateConfig:{name}";
            await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(1000, 5000)));
            if (!_memoryCache.TryGetValue<ClientUpdateConfigModel>(key, out var cacheValue)||cacheValue==null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(100, 5000)));
                if (!_memoryCache.TryGetValue(key, out cacheValue) || cacheValue == null)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(100, 5000)));
                    await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
                    cacheValue = await
                        dbContext
                            .ClientUpdateConfigurationDbSet
                            .Where(x => x.Status == ClientUpdateStatus.Public)
                            .Where(x => x.Name == name)
                            .OrderByDescending(static x => x.Version)
                            .FirstOrDefaultAsync();
                    if (cacheValue != null)
                    {
                        _memoryCache.Set(key, cacheValue, TimeSpan.FromMinutes(1));
                    }
                }
            }

            if (cacheValue != null)
            {
                clientUpdateConfig = cacheValue;
            }


            if (clientUpdateConfig != null && clientUpdateConfig.DnsFilters != null &&
                clientUpdateConfig.DnsFilters.Any())
            {
                await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
                var ipAddress = HttpContext.Connection.RemoteIpAddress.ToString();


                if (clientUpdateConfig.DnsFilterType == "include")
                    foreach (var filter in clientUpdateConfig.DnsFilters)
                    {
                        var nodeInfo = await dbContext.NodeInfoDbSet.FirstOrDefaultAsync(x => x.Name == filter.Value);
                        if (nodeInfo == null) continue;
                        if (nodeInfo.Profile.IpAddress == ipAddress)
                        {
                            apiResponse.SetResult(clientUpdateConfig);
                            break;
                        }
                    }
                else if (clientUpdateConfig.DnsFilterType == "exclude")
                    foreach (var filter in clientUpdateConfig.DnsFilters)
                    {
                        var nodeInfo = await dbContext.NodeInfoDbSet.FirstOrDefaultAsync(x => x.Name == filter.Value);
                        if (nodeInfo == null) continue;
                        if (nodeInfo.Profile.IpAddress == ipAddress)
                        {
                            apiResponse.SetResult(null);
                            break;
                        }
                    }
            }
            else
            {
                apiResponse.SetResult(clientUpdateConfig);
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }

        return apiResponse;
    }

    [HttpPost("/api/clientupdate/addorupdate")]
    public async Task<ApiResponse> AddOrUpdateAsync([FromBody] ClientUpdateConfigModel model)
    {
        var apiResponse = new ApiResponse();
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
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
            var key = $"ClientUpdateConfig:{model.Name}";
            _memoryCache.Remove(key);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogInformation(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }

        return apiResponse;
    }

    [HttpGet("/api/clientupdate/list")]
    public async Task<PaginationResponse<ClientUpdateConfigModel>> QueryClientUpdateListAsync(
        [FromQuery] PaginationQueryParameters queryParameters
    )
    {
        var apiResponse = new PaginationResponse<ClientUpdateConfigModel>();
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

            var pageIndex = queryParameters.PageIndex - 1;
            var pageSize = queryParameters.PageSize;

            IQueryable<ClientUpdateConfigModel> queryable = dbContext.ClientUpdateConfigurationDbSet;

            queryable = queryable.Include(x => x.PackageConfig).AsSplitQuery();

            apiResponse = await queryable.QueryPageItemsAsync(queryParameters);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogInformation(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }

        return apiResponse;
    }

    [HttpPost("/api/clientupdate/remove")]
    public async Task<ApiResponse> RemoveAsync([FromBody] ClientUpdateConfigModel model)
    {
        var apiResponse = new ApiResponse();
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            dbContext.ClientUpdateConfigurationDbSet.Remove(model);
            await dbContext.SaveChangesAsync();
            var key = $"ClientUpdateConfig:{model.Name}";
            _memoryCache.Remove(key);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }

        return apiResponse;
    }
}