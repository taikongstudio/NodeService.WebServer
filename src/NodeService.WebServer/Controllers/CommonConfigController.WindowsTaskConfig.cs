using NodeService.WebServer.Services.QueryOptimize;

namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpGet("/api/CommonConfig/WindowsTask/List")]
    public Task<PaginationResponse<WindowsTaskConfigModel>> QueryWindowsTasksListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<WindowsTaskConfigModel>(queryParameters);
    }

    [HttpPost("/api/CommonConfig/WindowsTask/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync(WindowsTaskConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model);
    }


    [HttpPost("/api/CommonConfig/WindowsTask/Remove")]
    public Task<ApiResponse> RemoveAsync(WindowsTaskConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }

    [HttpGet("/api/CommonConfig/WindowsTask/VersionList")]
    public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryWindowsTaskVersionListAsync(
[FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters, cancellationToken: cancellationToken);
    }

    [HttpPost("/api/CommonConfig/WindowsTask/SwitchVersion")]
    public Task<ApiResponse> SwitchWindowsTaskVersionAsync(
        [FromBody] ConfigurationVersionSwitchParameters parameters, CancellationToken cancellationToken = default)
    {
        return SwitchConfigurationVersionAsync<WindowsTaskConfigModel>(parameters, cancellationToken: cancellationToken);
    }

    [HttpPost("/api/CommonConfig/WindowsTask/DeleteVersion")]
    public Task<ApiResponse> DeleteWindowsTaskVersionAsync(
    [FromBody] ConfigurationVersionRecordModel entity, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationVersionAsync<WindowsTaskConfigModel>(new ConfigurationVersionDeleteParameters(entity), cancellationToken);
    }
}