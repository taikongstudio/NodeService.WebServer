using NodeService.Infrastructure.Data;
using NodeService.WebServer.Services.DataServices;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public partial class ConfigurationController : Controller
{
    readonly ExceptionCounter _exceptionCounter;
    readonly ConfigurationQueryService _configurationQueryService;
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
        ConfigurationQueryService  configurationQueryService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _memoryCache = memoryCache;
        _webServerOptions = optionSnapshot.Value;
        _exceptionCounter = exceptionCounter;
        _configurationQueryService = configurationQueryService;
    }

    public async Task<PaginationResponse<T>> QueryConfigurationListAsync<T>(
         PaginationQueryParameters queryParameters,
         CancellationToken cancellationToken = default)
         where T : JsonRecordBase, new()
    {
        return await QueryConfigurationListAsync<T>(queryParameters, null, cancellationToken);
    }

    public async Task<PaginationResponse<T>> QueryConfigurationListAsync<T>(
         PaginationQueryParameters queryParameters,
         Func<ListQueryResult<T>, CancellationToken, ValueTask>? func = null,
         CancellationToken cancellationToken = default)
         where T : JsonRecordBase, new()
    {
        var rsp = new PaginationResponse<T>();
        try
        {
            var result = await _configurationQueryService.QueryConfigurationByQueryParametersAsync<T>(queryParameters, cancellationToken);
            if (func != null)
            {
                await func(result, cancellationToken);
            }
            rsp.SetResult(result);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
        return rsp;
    }

    public async Task<ApiResponse<T>> QueryConfigurationAsync<T>(
        string id,
        Func<T?, CancellationToken, ValueTask>? func = null,
        CancellationToken cancellationToken = default)
        where T : JsonRecordBase
    {
        var rsp = new ApiResponse<T>();
        try
        {
            var result = await _configurationQueryService.QueryConfigurationByIdListAsync<T>([id], cancellationToken);

            if (result.HasValue && func != null)
            {
                foreach (var item in result.Items)
                {
                    await func(item, cancellationToken);
                }
            }
            rsp.SetResult(result.Items.FirstOrDefault());
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
        return rsp;
    }

    public async Task<ApiResponse> DeleteConfigurationAsync<T>(
        T model,
        Func<ConfigurationSaveChangesResult, CancellationToken, ValueTask>? changesFunc = null,
        CancellationToken cancellationToken = default)
        where T : JsonRecordBase
    {
        var rsp = new ApiResponse();
        try
        {
            var result = await _configurationQueryService.DeleteConfigurationAsync<T>(model, cancellationToken);
            if (changesFunc != null)
            {
                await changesFunc.Invoke(result, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
        return rsp;

    }

    public async Task<ApiResponse> AddOrUpdateConfigurationAsync<T>(
        T model,
        Func<ConfigurationSaveChangesResult, CancellationToken, ValueTask>? changesFunc = null,
        CancellationToken cancellationToken = default)
        where T : JsonRecordBase
    {
        var rsp = new ApiResponse();
        try
        {
            var result = await _configurationQueryService.AddOrUpdateConfigurationAsync<T>(
                model,
                false,
                cancellationToken);
            if (changesFunc != null)
            {
                await changesFunc.Invoke(result, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
            rsp.ErrorCode = ex.HResult;
            rsp.Message = ex.ToString();
        }
        return rsp;

    }

    public async Task<ApiResponse> SwitchConfigurationVersionAsync<T>(
         ConfigurationVersionSwitchParameters parameters,
         Func<ConfigurationSaveChangesResult, CancellationToken, ValueTask>? func = null,
         CancellationToken cancellationToken = default)
         where T : JsonRecordBase
    {
        var rsp = new ApiResponse();
        try
        {
            var result = await _configurationQueryService.SwitchConfigurationVersionAsync<T>(parameters, cancellationToken);
            if (func != null)
            {
                await func.Invoke(result, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
        return rsp;

    }

    public async Task<ApiResponse> DeleteConfigurationVersionAsync<T>(
         ConfigurationVersionDeleteParameters parameters,
         CancellationToken cancellationToken = default)
         where T : JsonRecordBase
    {
        var rsp = new ApiResponse();
        try
        {
            var result = await _configurationQueryService.DeleteConfigurationVersionAsync<T>(parameters, cancellationToken);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
        return rsp;
    }

    public async Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryConfigurationVersionListAsync<T>(
    PaginationQueryParameters queryParameters,
    CancellationToken cancellationToken = default)
    where T : JsonRecordBase
    {
        var rsp = new PaginationResponse<ConfigurationVersionRecordModel>();
        try
        {
            var result = await _configurationQueryService.QueryConfigurationVersionListByQueryParametersAsync(queryParameters, cancellationToken);
            rsp.SetResult(result);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
        return rsp;
    }

}