namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public partial class CommonConfigController : Controller
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ILogger<CommonConfigController> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly INodeSessionService _nodeSessionService;
    private readonly IAsyncQueue<NotificationMessage> _notificationMessageQueue;
    private readonly IServiceProvider _serviceProvider;
    private readonly IVirtualFileSystem _virtualFileSystem;
    private readonly WebServerOptions _webServerOptions;

    public CommonConfigController(
        IMemoryCache memoryCache,
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        ILogger<CommonConfigController> logger,
        IVirtualFileSystem virtualFileSystem,
        IOptionsSnapshot<WebServerOptions> optionSnapshot,
        IServiceProvider serviceProvider,
        INodeSessionService nodeSessionService,
        IAsyncQueue<NotificationMessage> notificationMessageQueue)
    {
        _serviceProvider = serviceProvider;
        _dbContextFactory = dbContextFactory;
        _memoryCache = memoryCache;
        _virtualFileSystem = virtualFileSystem;
        _webServerOptions = optionSnapshot.Value;
        _logger = logger;
        _nodeSessionService = nodeSessionService;
        _notificationMessageQueue = notificationMessageQueue;
    }

    private async Task<PaginationResponse<T>> QueryConfigurationListAsync<T>(PaginationQueryParameters queryParameters)
        where T : ConfigurationModel, new()
    {
        PaginationResponse<T> apiResponse = new PaginationResponse<T>();

        try
        {
            _logger.LogInformation($"{typeof(T)}:{queryParameters}");
            if (queryParameters.QueryStrategy == QueryStrategy.Query)
            {
                apiResponse = await QueryAsync<T>(queryParameters);
            }
            else if (queryParameters.QueryStrategy == QueryStrategy.PreferCache)
            {
                var key = $"{typeof(T).FullName}:{queryParameters}";
                if (!_memoryCache.TryGetValue<PaginationResponse<T>>(key, out var cacheValue) || cacheValue == null)
                {
                    cacheValue = await QueryAsync<T>(queryParameters);
                    if (cacheValue != null)
                    {
                        apiResponse = cacheValue;
                        _memoryCache.Set(key, cacheValue, TimeSpan.FromSeconds(10));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }
        return apiResponse;
    }

    private async Task<PaginationResponse<T>> QueryAsync<T>(PaginationQueryParameters queryParameters) where T : ConfigurationModel, new()
    {
        var apiResponse = new PaginationResponse<T>();
        var dbContext = _dbContextFactory.CreateDbContext();
        var name = typeof(T).Name;
        IQueryable<T> queryable = dbContext.GetDbSet<T>();
        var pageSize = queryParameters.PageSize;
        var pageIndex = queryParameters.PageIndex - 1;
        var startIndex = pageSize * pageIndex;

        if (!string.IsNullOrEmpty(queryParameters.Keywords))
            queryable = queryable.Where(x =>
                x.Name == queryParameters.Keywords || x.Name.Contains(queryParameters.Keywords));

        queryable = queryable.OrderByDescending(x => x.ModifiedDateTime);


        var totalCount = await queryable.CountAsync();
        List<T> items = [];
        if (pageSize <= 0 && pageIndex <= 0)
        {
            items = await queryable.ToListAsync();
        }
        else
        {
            items = await queryable.Skip(startIndex)
                            .Take(pageSize)
                            .ToListAsync();
        }

        var pageCount = totalCount > 0 ? Math.DivRem(totalCount, pageSize, out var _) + 1 : 0;
        if (queryParameters.PageIndex > pageCount) queryParameters.PageIndex = pageCount;


        apiResponse.TotalCount = totalCount;
        apiResponse.Result = items;
        apiResponse.PageIndex = queryParameters.PageIndex;
        apiResponse.PageSize = queryParameters.PageSize;
        return apiResponse;
    }

    private async Task<ApiResponse<T>> QueryConfigurationAsync<T>(string id, Func<T?, Task>? func = null)
        where T : ConfigurationModel
    {
        var apiResponse = new ApiResponse<T>();
        try
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            apiResponse.Result = await dbContext.GetDbSet<T>().AsQueryable().FirstOrDefaultAsync(x => x.Id == id);
            if (apiResponse.Result != null && func != null) await func.Invoke(apiResponse.Result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }

        return apiResponse;
    }

    private async Task<ApiResponse> DeleteConfigurationAsync<T>(T model, Func<T, Task>? changesFunc = null)
        where T : ConfigurationModel
    {
        var apiResponse = new ApiResponse();
        try
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            dbContext.GetDbSet<T>().Remove(model);
            var changes = await dbContext.SaveChangesAsync();
            if (changes > 0 && changesFunc != null) await changesFunc.Invoke(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }

        return apiResponse;
    }

    private async Task<ApiResponse> AddOrUpdateConfigurationAsync<T>(T model, Func<T, Task>? changesFunc = null)
        where T : ConfigurationModel
    {
        var apiResponse = new ApiResponse();
        try
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var modelFromDb = await dbContext.GetDbSet<T>().FindAsync(model.Id);
            if (modelFromDb == null)
            {
                model.EntityVersion = Guid.NewGuid().ToByteArray();
                await dbContext
                    .GetDbSet<T>().AddAsync(model);
            }
            else
            {
                modelFromDb.With(model);
                modelFromDb.EntityVersion = Guid.NewGuid().ToByteArray();
            }

            var changes = await dbContext.SaveChangesAsync();
            if (changes > 0 && changesFunc != null) await changesFunc.Invoke(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }

        return apiResponse;
    }
}