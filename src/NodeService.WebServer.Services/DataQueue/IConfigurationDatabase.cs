
namespace NodeService.WebServer.Services.DataQueue
{
    public interface IConfigurationDatabase
    {
        Task<ApiResponse> AddOrUpdateConfigurationAsync<T>(T model, Func<ConfigurationSaveChangesResult, CancellationToken, ValueTask>? changesFunc = null, CancellationToken cancellationToken = default) where T : JsonRecordBase;
        Task<ApiResponse> DeleteConfigurationAsync<T>(T model, Func<ConfigurationSaveChangesResult, CancellationToken, ValueTask>? changesFunc = null, CancellationToken cancellationToken = default) where T : JsonRecordBase;
        Task<ApiResponse> DeleteConfigurationVersionAsync<T>(ConfigurationVersionDeleteParameters parameters, CancellationToken cancellationToken = default) where T : JsonRecordBase;
        Task<ApiResponse<T>> QueryConfigurationAsync<T>(string id, Func<T?, CancellationToken, ValueTask>? func = null, CancellationToken cancellationToken = default) where T : JsonRecordBase;
        Task<PaginationResponse<T>> QueryConfigurationListAsync<T>(PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default) where T : JsonRecordBase, new();
        Task<PaginationResponse<T>> QueryConfigurationVersionListAsync<T>(PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default) where T : JsonRecordBase;
        Task<ApiResponse> SwitchConfigurationVersionAsync<T>(ConfigurationVersionSwitchParameters parameters, Func<ConfigurationSaveChangesResult, CancellationToken, ValueTask>? func = null, CancellationToken cancellationToken = default) where T : JsonRecordBase;
    }
}