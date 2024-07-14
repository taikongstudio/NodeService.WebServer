using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.Models;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.DataQueue;
using System;
using System.Threading;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public partial class ConfigurationController : Controller
{
    readonly ExceptionCounter _exceptionCounter;

    readonly BatchQueue<AsyncOperation<ConfigurationQueryQueueServiceParameters, ConfigurationQueryQueueServiceResult>>
        _batchQueue;
    readonly ConfigurationDatabase _configurationDatabase;
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
        ConfigurationDatabase configurationDatabase)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _memoryCache = memoryCache;
        _webServerOptions = optionSnapshot.Value;
        _exceptionCounter = exceptionCounter;
        _configurationDatabase = configurationDatabase;
    }

    public Task<PaginationResponse<T>> QueryConfigurationListAsync<T>(
         PaginationQueryParameters queryParameters,
         CancellationToken cancellationToken = default)
         where T : JsonRecordBase, new()
    {

        return _configurationDatabase.QueryConfigurationListAsync<T>(queryParameters, cancellationToken);
    }

    public Task<ApiResponse<T>> QueryConfigurationAsync<T>(
        string id,
        Func<T?, CancellationToken, ValueTask>? func = null,
        CancellationToken cancellationToken = default)
        where T : JsonRecordBase
    {
        return _configurationDatabase.QueryConfigurationAsync<T>(id, func, cancellationToken);
    }

    public Task<ApiResponse> DeleteConfigurationAsync<T>(
        T model,
        Func<ConfigurationSaveChangesResult, CancellationToken, ValueTask>? changesFunc = null,
        CancellationToken cancellationToken = default)
        where T : JsonRecordBase
    {
        return _configurationDatabase.DeleteConfigurationAsync<T>(model, changesFunc, cancellationToken);
    }

    public Task<ApiResponse> AddOrUpdateConfigurationAsync<T>(
        T model,
        Func<ConfigurationSaveChangesResult, CancellationToken, ValueTask>? changesFunc = null,
        CancellationToken cancellationToken = default)
        where T : JsonRecordBase
    {
        return _configurationDatabase.AddOrUpdateConfigurationAsync<T>(model, changesFunc, cancellationToken);
    }

    public Task<ApiResponse> SwitchConfigurationVersionAsync<T>(
         ConfigurationVersionSwitchParameters parameters,
         Func<ConfigurationSaveChangesResult, CancellationToken, ValueTask>? func = null,
         CancellationToken cancellationToken = default)
         where T : JsonRecordBase
    {
        return _configurationDatabase.SwitchConfigurationVersionAsync<T>(parameters, func, cancellationToken);
    }

    public Task<ApiResponse> DeleteConfigurationVersionAsync<T>(
         ConfigurationVersionDeleteParameters parameters,
         CancellationToken cancellationToken = default)
         where T : JsonRecordBase
    {
        return _configurationDatabase.DeleteConfigurationVersionAsync<T>(parameters, cancellationToken);
    }

    public Task<PaginationResponse<T>> QueryConfigurationVersionListAsync<T>(
    PaginationQueryParameters queryParameters,
    CancellationToken cancellationToken = default)
    where T : JsonRecordBase
    {
        return _configurationDatabase.QueryConfigurationVersionListAsync<T>(queryParameters, cancellationToken);
    }

}