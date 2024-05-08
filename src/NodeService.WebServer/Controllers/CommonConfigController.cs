﻿using NodeService.WebServer.Models;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public partial class CommonConfigController : Controller
{
    readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    readonly ILogger<CommonConfigController> _logger;
    readonly IMemoryCache _memoryCache;
    readonly INodeSessionService _nodeSessionService;
    readonly IAsyncQueue<NotificationMessage> _notificationMessageQueue;
    readonly IServiceProvider _serviceProvider;
    readonly IVirtualFileSystem _virtualFileSystem;
    readonly WebServerOptions _webServerOptions;
    readonly ExceptionCounter _exceptionCounter;

    public CommonConfigController(
        IMemoryCache memoryCache,
        ExceptionCounter exceptionCounter,
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

    async Task<PaginationResponse<T>> QueryConfigurationListAsync<T>(PaginationQueryParameters queryParameters)
        where T : ConfigurationModel, new()
    {
        PaginationResponse<T> apiResponse = new PaginationResponse<T>();

        try
        {
            _logger.LogInformation($"{typeof(T)}:{queryParameters}");
            if (queryParameters.QueryStrategy == QueryStrategy.QueryPreferred)
            {
                apiResponse = await QueryAsync<T>(queryParameters);
            }
            else if (queryParameters.QueryStrategy == QueryStrategy.CachePreferred)
            {
                _logger.LogInformation($"{queryParameters}");
                var key = $"{typeof(T).FullName}:{queryParameters}";
                PaginationResponse<T>? cacheValue = null;
                if (!_memoryCache.TryGetValue(key, out cacheValue) || cacheValue == null)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(100, 5000)));
                    if (!_memoryCache.TryGetValue(key, out cacheValue) || cacheValue == null)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(100, 5000)));
                        cacheValue = await QueryAsync<T>(queryParameters);
                        if (cacheValue != null)
                        {
                            _memoryCache.Set(key, cacheValue, TimeSpan.FromMinutes(1));
                        }
                    }
                }
                if (cacheValue != null)
                {
                    apiResponse = cacheValue;
                }

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

    async Task<PaginationResponse<T>> QueryAsync<T>(PaginationQueryParameters queryParameters) where T : ConfigurationModel, new()
    {
        var dbContext = await _dbContextFactory.CreateDbContextAsync();
        IQueryable<T> queryable = dbContext.GetDbSet<T>();

        if (!string.IsNullOrEmpty(queryParameters.Keywords))
            queryable = queryable.Where(x =>
                x.Name == queryParameters.Keywords || x.Name.Contains(queryParameters.Keywords));

        queryable = queryable.OrderByDescending(x => x.ModifiedDateTime);

        queryable = queryable.OrderBy(queryParameters.SortDescriptions);
        var apiResponse = await queryable.QueryPageItemsAsync(queryParameters);
        return apiResponse;
    }

    async Task<ApiResponse<T>> QueryConfigurationAsync<T>(string id, Func<T?, Task>? func = null)
        where T : ConfigurationModel
    {
        var apiResponse = new ApiResponse<T>();
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            apiResponse.SetResult(await dbContext.GetDbSet<T>().AsQueryable().FirstOrDefaultAsync(x => x.Id == id));
            if (apiResponse.Result != null && func != null) await func.Invoke(apiResponse.Result);
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

    async Task<ApiResponse> DeleteConfigurationAsync<T>(T model, Func<T, Task>? changesFunc = null)
        where T : ConfigurationModel
    {
        var apiResponse = new ApiResponse();
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
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

    async Task<ApiResponse> AddOrUpdateConfigurationAsync<T>(T model, Func<T, Task>? changesFunc = null)
        where T : ConfigurationModel
    {
        var apiResponse = new ApiResponse();
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
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
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }

        return apiResponse;
    }
}