using NodeService.WebServer.Services.DataQueue;

namespace NodeService.WebServer.Controllers;

public partial class ConfigurationController
{
    [HttpPost("/api/CommonConfig/Database/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] DatabaseConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model);
    }

    [HttpGet("/api/CommonConfig/Database/List")]
    public Task<PaginationResponse<DatabaseConfigModel>> QueryDatabaseConfigurationListAsync(
        [FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationListAsync<DatabaseConfigModel>(queryParameters, cancellationToken);
    }

    [HttpGet("/api/CommonConfig/Database/{id}")]
    public Task<ApiResponse<DatabaseConfigModel>> QueryDatabaseConfigAsync(string id, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationAsync<DatabaseConfigModel>(id, cancellationToken: cancellationToken);
    }

    [HttpPost("/api/CommonConfig/Database/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] DatabaseConfigModel model, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationAsync(model, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/CommonConfig/Database/VersionList")]
    public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryDatabaseConfigurationVersionListAsync(
    [FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters, cancellationToken: cancellationToken);
    }

    [HttpPost("/api/CommonConfig/Database/SwitchVersion")]
    public Task<ApiResponse> SwitchDatabaseConfigurationVersionAsync(
        [FromBody] ConfigurationVersionSwitchParameters parameters, CancellationToken cancellationToken = default)
    {
        return SwitchConfigurationVersionAsync<DatabaseConfigModel>(parameters, cancellationToken: cancellationToken);
    }

    [HttpPost("/api/CommonConfig/Database/DeleteVersion")]
    public Task<ApiResponse> DeleteDatabaseConfigurationVersionAsync(
    [FromBody] ConfigurationVersionRecordModel entity, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationVersionAsync<DatabaseConfigModel>(new ConfigurationVersionDeleteParameters(entity), cancellationToken);
    }
}