using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.DataQueue;
using System.Threading;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public partial class ConfigurationController : Controller
{
    readonly ExceptionCounter _exceptionCounter;

    readonly BatchQueue<BatchQueueOperation<ConfigurationQueryQueueServiceParameters, ConfigurationQueryQueueServiceResult>>
        _batchQueue;

    readonly ILogger<ConfigurationController> _logger;
    readonly IMemoryCache _memoryCache;
    readonly IServiceProvider _serviceProvider;
    readonly WebServerOptions _webServerOptions;

    public ConfigurationController(
        ILogger<ConfigurationController> logger,
        IMemoryCache memoryCache,
        ExceptionCounter exceptionCounter,
        IOptionsSnapshot<WebServerOptions> optionSnapshot,
        IServiceProvider serviceProvider,
        BatchQueue<BatchQueueOperation<ConfigurationQueryQueueServiceParameters, ConfigurationQueryQueueServiceResult>> batchQueue
    )
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _memoryCache = memoryCache;
        _webServerOptions = optionSnapshot.Value;
        _exceptionCounter = exceptionCounter;
        _batchQueue = batchQueue;
    }

    private async Task<PaginationResponse<T>> QueryConfigurationListAsync<T>(
        PaginationQueryParameters queryParameters,
        CancellationToken cancellationToken = default)
        where T : JsonRecordBase, new()
    {
        var apiResponse = new PaginationResponse<T>();

        try
        {
            _logger.LogInformation($"{typeof(T)}:{queryParameters}");
            ListQueryResult<T> result = default;
            var paramters = new ConfigurationQueryQueueServiceParameters(typeof(T), new ConfigurationPaginationQueryParameters(queryParameters));
            var priority = queryParameters.QueryStrategy == QueryStrategy.QueryPreferred
                ? BatchQueueOperationPriority.High
                : BatchQueueOperationPriority.Normal;
            var op = new BatchQueueOperation<ConfigurationQueryQueueServiceParameters, ConfigurationQueryQueueServiceResult>(
                paramters,
                BatchQueueOperationKind.Query,
                priority);
            await _batchQueue.SendAsync(op, cancellationToken);
            var serviceResult = await op.WaitAsync(cancellationToken);
            var queryResult = serviceResult.Value.AsT0;
            if (queryResult.HasValue)
            {
                result = new ListQueryResult<T>(
                                    queryResult.TotalCount,
                                    queryResult.PageIndex,
                                    queryResult.PageSize,
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
        Func<T?, CancellationToken, ValueTask>? func = null,
        CancellationToken cancellationToken = default)
        where T : JsonRecordBase
    {
        var apiResponse = new ApiResponse<T>();
        try
        {
            _logger.LogInformation($"{typeof(T)}:{id}");
            var paramters = new ConfigurationQueryQueueServiceParameters(typeof(T), new ConfigurationIdentityListQueryParameters([id]));
            var op = new BatchQueueOperation<ConfigurationQueryQueueServiceParameters, ConfigurationQueryQueueServiceResult>(paramters,
                BatchQueueOperationKind.Query);
            await _batchQueue.SendAsync(op);
            var serviceResult = await op.WaitAsync(cancellationToken);
            var queryResult = serviceResult.Value.AsT0;
            if (queryResult.HasValue) apiResponse.SetResult(queryResult.Items.FirstOrDefault() as T);
            if (apiResponse.Result != null && func != null) await func.Invoke(apiResponse.Result, cancellationToken);
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

    private async Task<ApiResponse> DeleteConfigurationAsync<T>(
        T model,
        Func<ConfigurationSaveChangesResult, CancellationToken, ValueTask>? changesFunc = null,
        CancellationToken cancellationToken = default)
        where T : JsonRecordBase
    {
        var apiResponse = new ApiResponse();
        try
        {
            var paramters = new ConfigurationQueryQueueServiceParameters(typeof(T), new ConfigurationAddUpdateDeleteParameters(model));
            var op = new BatchQueueOperation<ConfigurationQueryQueueServiceParameters, ConfigurationQueryQueueServiceResult>(paramters,
                BatchQueueOperationKind.Delete);
            await _batchQueue.SendAsync(op);
            var serviceResult = await op.WaitAsync(cancellationToken);
            var queryResult = serviceResult.Value.AsT1;
            if (queryResult.ChangesCount > 0)
            {
                if (changesFunc != null)
                {
                    await changesFunc.Invoke(queryResult, cancellationToken);
                }
                var eventQueue = _serviceProvider.GetService<IAsyncQueue<ConfigurationChangedEvent>>();
                await eventQueue.EnqueueAsync(new ConfigurationChangedEvent()
                {
                    ChangedType = ConfigurationChangedType.Delete,
                    TypeName = typeof(T).FullName,
                    Id = model.Id,
                    Json = model.ToJson<T>(),
                    NodeIdList = []
                });
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

    private async Task<ApiResponse> AddOrUpdateConfigurationAsync<T>(
        T model,
        Func<ConfigurationSaveChangesResult, CancellationToken, ValueTask>? changesFunc = null,
        CancellationToken cancellationToken = default)
        where T : JsonRecordBase
    {
        var apiResponse = new ApiResponse();
        try
        {
            var paramters = new ConfigurationQueryQueueServiceParameters(typeof(T), new ConfigurationAddUpdateDeleteParameters(model));
            var op = new BatchQueueOperation<ConfigurationQueryQueueServiceParameters, ConfigurationQueryQueueServiceResult>(paramters,
                BatchQueueOperationKind.AddOrUpdate);
            await _batchQueue.SendAsync(op);
            var serviceResult = await op.WaitAsync(cancellationToken);
            var saveChangesResult = serviceResult.Value.AsT1;
            if (saveChangesResult.ChangesCount > 0)
            {
                if (changesFunc != null)
                {
                    await changesFunc.Invoke(saveChangesResult, cancellationToken);
                }
                var eventQueue = _serviceProvider.GetService<IAsyncQueue<ConfigurationChangedEvent>>();
                await eventQueue.EnqueueAsync(new ConfigurationChangedEvent()
                {
                    NodeIdList = model is INodeIdentityListProvider nodeIdentityListProvider ? nodeIdentityListProvider.GetNodeIdentityList() : [],
                    ChangedType = saveChangesResult.Type,
                    TypeName = typeof(T).FullName,
                    Id = model.Id,
                    Json = saveChangesResult.NewValue.ToJson<T>()
                }, cancellationToken);
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

    async Task<ApiResponse> SwitchConfigurationVersionAsync<T>(
        ConfigurationVersionSwitchParameters parameters,
        Func<ConfigurationSaveChangesResult, CancellationToken, ValueTask>? func = null,
        CancellationToken cancellationToken = default)
        where T : JsonRecordBase
    {
        var apiResponse = new ApiResponse();
        try
        {
            var paramters = new ConfigurationQueryQueueServiceParameters(typeof(T), parameters);
            var op = new BatchQueueOperation<ConfigurationQueryQueueServiceParameters, ConfigurationQueryQueueServiceResult>(paramters,
                BatchQueueOperationKind.AddOrUpdate);
            await _batchQueue.SendAsync(op);
            var serviceResult = await op.WaitAsync(cancellationToken);
            var queryResult = serviceResult.Value.AsT1;
            if (func != null)
            {
                await func.Invoke(queryResult, cancellationToken);
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

    async Task<ApiResponse> DeleteConfigurationVersionAsync<T>(
        ConfigurationVersionDeleteParameters parameters,
        CancellationToken cancellationToken = default)
        where T : JsonRecordBase
    {
        var apiResponse = new ApiResponse();
        try
        {
            var entity = parameters.Value;
            var paramters = new ConfigurationQueryQueueServiceParameters(typeof(T), parameters);
            var op = new BatchQueueOperation<ConfigurationQueryQueueServiceParameters, ConfigurationQueryQueueServiceResult>(paramters,
                BatchQueueOperationKind.Delete);
            await _batchQueue.SendAsync(op);
            var serviceResult = await op.WaitAsync(cancellationToken);
            var queryResult = serviceResult.Value.AsT2;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }

        return apiResponse;
    }

    private async Task<PaginationResponse<T>> QueryConfigurationVersionListAsync<T>(
    PaginationQueryParameters queryParameters,
    CancellationToken cancellationToken = default)
    where T : JsonRecordBase
    {
        var apiResponse = new PaginationResponse<T>();

        try
        {
            _logger.LogInformation($"{typeof(T)}:{queryParameters}");
            ListQueryResult<T> result = default;
            var paramters = new ConfigurationQueryQueueServiceParameters(typeof(T), new ConfigurationVersionPaginationQueryParameters(queryParameters));
            var priority = queryParameters.QueryStrategy == QueryStrategy.QueryPreferred
                ? BatchQueueOperationPriority.High
                : BatchQueueOperationPriority.Normal;
            var op = new BatchQueueOperation<ConfigurationQueryQueueServiceParameters, ConfigurationQueryQueueServiceResult>(
                paramters,
                BatchQueueOperationKind.Query,
                priority);
            await _batchQueue.SendAsync(op);
            var serviceResult = await op.WaitAsync(cancellationToken);
            var queryResult = serviceResult.Value.AsT0;
            if (queryResult.HasValue)
                result = new ListQueryResult<T>(
                    queryResult.TotalCount,
                    queryResult.PageIndex,
                    queryResult.PageSize,
                    queryResult.Items.Select(static x => (T)x));
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
}