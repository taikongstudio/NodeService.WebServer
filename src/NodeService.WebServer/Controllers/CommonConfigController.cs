using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.Models;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.Queries;
using System.Threading;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public partial class CommonConfigController : Controller
{
    private readonly ExceptionCounter _exceptionCounter;
    private readonly BatchQueue<BatchQueueOperation<CommonConfigBatchQueueOperationParameters, ListQueryResult<object>>> _batchQueue;
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
        IAsyncQueue<NotificationMessage> notificationMessageQueue,
        BatchQueue<BatchQueueOperation<CommonConfigBatchQueueOperationParameters, ListQueryResult<object>>> batchQueue
        )
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _memoryCache = memoryCache;
        _webServerOptions = optionSnapshot.Value;
        _nodeSessionService = nodeSessionService;
        _notificationMessageQueue = notificationMessageQueue;
        _exceptionCounter = exceptionCounter;
        _batchQueue = batchQueue;
    }

    private async Task<PaginationResponse<T>> QueryConfigurationListAsync<T>(PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
        where T : JsonBasedDataModel, new()
    {
        var apiResponse = new PaginationResponse<T>();

        try
        {
            _logger.LogInformation($"{typeof(T)}:{queryParameters}");
            ListQueryResult<T> result = default;
            var paramters = new CommonConfigBatchQueueOperationParameters(typeof(T), queryParameters);
            var op = new BatchQueueOperation<CommonConfigBatchQueueOperationParameters, ListQueryResult<object>>(paramters, BatchQueueOperationKind.Query);
            await _batchQueue.SendAsync(op);
            var queryResult = await op.WaitAsync(cancellationToken);
            if (queryResult.HasValue)
            {
                result = new ListQueryResult<T>(
                    queryResult.TotalCount,
                    queryResult.PageSize,
                    queryResult.PageIndex,
                    queryResult.Items.Select(static x => (T)x));
            }
            if (result.HasValue) apiResponse.SetResult(result);
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

    private async Task<ApiResponse<T>> QueryConfigurationAsync<T>(
        string id,
        Func<T?, Task>? func = null,
        CancellationToken cancellationToken = default)
        where T : JsonBasedDataModel
    {
        var apiResponse = new ApiResponse<T>();
        try
        {
            _logger.LogInformation($"{typeof(T)}:{id}");
            ListQueryResult<T> result = default;
            var paramters = new CommonConfigBatchQueueOperationParameters(typeof(T), id);
            var op = new BatchQueueOperation<CommonConfigBatchQueueOperationParameters, ListQueryResult<object>>(paramters, BatchQueueOperationKind.Query);
            await _batchQueue.SendAsync(op);
            var queryResult = await op.WaitAsync(cancellationToken);
            if (queryResult.HasValue)
            {
                apiResponse.SetResult(queryResult.Items.FirstOrDefault() as T);
            }
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