using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public partial class CommonConfigController : Controller
{
    private readonly ExceptionCounter _exceptionCounter;
    private readonly ILogger<CommonConfigController> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly INodeSessionService _nodeSessionService;
    private readonly IAsyncQueue<NotificationMessage> _notificationMessageQueue;
    private readonly IServiceProvider _serviceProvider;
    private readonly WebServerOptions _webServerOptions;

    public CommonConfigController(
        ILogger<CommonConfigController> logger,
        IMemoryCache memoryCache,
        ExceptionCounter exceptionCounter,
        IOptionsSnapshot<WebServerOptions> optionSnapshot,
        IServiceProvider serviceProvider,
        INodeSessionService nodeSessionService,
        IAsyncQueue<NotificationMessage> notificationMessageQueue)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _memoryCache = memoryCache;
        _webServerOptions = optionSnapshot.Value;
        _nodeSessionService = nodeSessionService;
        _notificationMessageQueue = notificationMessageQueue;
        _exceptionCounter = exceptionCounter;
    }

    private async Task<PaginationResponse<T>> QueryConfigurationListAsync<T>(PaginationQueryParameters queryParameters)
        where T : JsonBasedDataModel, new()
    {
        var apiResponse = new PaginationResponse<T>();

        try
        {
            var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
            using var repo = repoFactory.CreateRepository();
            _logger.LogInformation($"{typeof(T)}:{queryParameters}");
            ListQueryResult<T> cachedQueryResult = default;
            if (queryParameters.QueryStrategy == QueryStrategy.QueryPreferred)
            {
                cachedQueryResult = await repo.PaginationQueryAsync(
                    new CommonConfigSpecification<T>(queryParameters.Keywords),
                    queryParameters.PageSize,
                    queryParameters.PageIndex);
            }
            else if (queryParameters.QueryStrategy == QueryStrategy.CachePreferred)
            {
                _logger.LogInformation($"{queryParameters}");
                var key = $"{typeof(T).FullName}:{queryParameters}";

                if (!_memoryCache.TryGetValue(key, out cachedQueryResult) || !cachedQueryResult.HasValue)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(100, 500)));
                    if (!_memoryCache.TryGetValue(key, out cachedQueryResult) || !cachedQueryResult.HasValue)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(100, 500)));
                        cachedQueryResult = await repo.PaginationQueryAsync(
                            new CommonConfigSpecification<T>(
                                queryParameters.Keywords,
                                queryParameters.SortDescriptions),
                            queryParameters.PageSize,
                            queryParameters.PageIndex);

                        if (cachedQueryResult.HasValue)
                            _memoryCache.Set(key, cachedQueryResult, TimeSpan.FromMinutes(1));
                    }
                }
            }

            if (cachedQueryResult.HasValue) apiResponse.SetResult(cachedQueryResult);
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

    private async Task<ApiResponse<T>> QueryConfigurationAsync<T>(string id, Func<T?, Task>? func = null)
        where T : JsonBasedDataModel
    {
        var apiResponse = new ApiResponse<T>();
        try
        {
            var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
            using var repo = repoFactory.CreateRepository();
            apiResponse.SetResult(await repo.GetByIdAsync(id));
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

    private async Task<ApiResponse> DeleteConfigurationAsync<T>(T model, Func<T, Task>? changesFunc = null)
        where T : JsonBasedDataModel
    {
        var apiResponse = new ApiResponse();
        try
        {
            var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
            using var repo = repoFactory.CreateRepository();
            await repo.DeleteAsync(model);
            if (repo.LastSaveChangesCount > 0 && changesFunc != null) await changesFunc.Invoke(model);
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
        where T : JsonBasedDataModel
    {
        var apiResponse = new ApiResponse();
        try
        {
            var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
            using var repo = repoFactory.CreateRepository();
            var modelFromDb = await repo.GetByIdAsync(model.Id);
            if (modelFromDb == null)
            {
                model.EntityVersion = Guid.NewGuid().ToByteArray();
                await repo.AddAsync(model);
            }
            else
            {
                modelFromDb.With(model);
                await repo.SaveChangesAsync();
            }

            if (repo.LastSaveChangesCount > 0 && changesFunc != null) await changesFunc.Invoke(model);
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