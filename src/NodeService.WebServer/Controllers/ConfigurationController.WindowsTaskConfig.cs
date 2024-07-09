using NodeService.WebServer.Services.DataQueue;

namespace NodeService.WebServer.Controllers;

public partial class ConfigurationController
{
    [HttpGet("/api/Configuration/WindowsTask/List")]
    public Task<PaginationResponse<WindowsTaskConfigModel>> QueryWindowsTasksListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<WindowsTaskConfigModel>(queryParameters);
    }

    [HttpPost("/api/Configuration/WindowsTask/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync(WindowsTaskConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model);
    }


    [HttpPost("/api/Configuration/WindowsTask/Remove")]
    public Task<ApiResponse> RemoveAsync(WindowsTaskConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }

    [HttpGet("/api/Configuration/WindowsTask/VersionList")]
    public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryWindowsTaskVersionListAsync(
[FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters, cancellationToken: cancellationToken);
    }

    [HttpPost("/api/Configuration/WindowsTask/SwitchVersion")]
    public Task<ApiResponse> SwitchWindowsTaskVersionAsync(
        [FromBody] ConfigurationVersionSwitchParameters parameters, CancellationToken cancellationToken = default)
    {
        return SwitchConfigurationVersionAsync<WindowsTaskConfigModel>(parameters, cancellationToken: cancellationToken);
    }

    [HttpPost("/api/Configuration/WindowsTask/DeleteVersion")]
    public Task<ApiResponse> DeleteWindowsTaskVersionAsync(
    [FromBody] ConfigurationVersionRecordModel entity, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationVersionAsync<WindowsTaskConfigModel>(new ConfigurationVersionDeleteParameters(entity), cancellationToken);
    }
}