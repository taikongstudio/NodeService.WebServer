using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.Models;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Models;
using static NuGet.Protocol.Core.Types.Repository;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public partial class CommonConfigController : Controller
{
    readonly ILogger<CommonConfigController> _logger;
    readonly IMemoryCache _memoryCache;
    readonly INodeSessionService _nodeSessionService;
    readonly IAsyncQueue<NotificationMessage> _notificationMessageQueue;
    readonly IServiceProvider _serviceProvider;
    readonly WebServerOptions _webServerOptions;
    readonly ExceptionCounter _exceptionCounter;

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

    async Task<PaginationResponse<T>> QueryConfigurationListAsync<T>(PaginationQueryParameters queryParameters)
        where T : ConfigurationModel, new()
    {
        PaginationResponse<T> apiResponse = new PaginationResponse<T>();

        try
        {
            ApplicationRepositoryFactory<T> repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
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
                        {
                            _memoryCache.Set(key, cachedQueryResult, TimeSpan.FromMinutes(1));
                        }
                    }
                }
            }
            if (cachedQueryResult.HasValue)
            {
                apiResponse.SetResult(cachedQueryResult);
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

    async Task<ApiResponse<T>> QueryConfigurationAsync<T>(string id, Func<T?, Task>? func = null)
        where T : ConfigurationModel
    {
        var apiResponse = new ApiResponse<T>();
        try
        {
            ApplicationRepositoryFactory<T> repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
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

    async Task<ApiResponse> DeleteConfigurationAsync<T>(T model, Func<T, Task>? changesFunc = null)
        where T : ConfigurationModel
    {
        var apiResponse = new ApiResponse();
        try
        {
            ApplicationRepositoryFactory<T> repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
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

    async Task<ApiResponse> AddOrUpdateConfigurationAsync<T>(T model, Func<T, Task>? changesFunc = null)
        where T : ConfigurationModel
    {
        var apiResponse = new ApiResponse();
        try
        {
            ApplicationRepositoryFactory<T> repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
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