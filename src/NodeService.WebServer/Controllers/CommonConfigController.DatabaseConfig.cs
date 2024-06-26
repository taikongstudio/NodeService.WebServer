namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/CommonConfig/Database/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] DatabaseConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model);
    }

    [HttpGet("/api/CommonConfig/Database/List")]
    public Task<PaginationResponse<DatabaseConfigModel>> QueryDatabaseConfigurationListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<DatabaseConfigModel>(queryParameters);
    }

    [HttpGet("/api/CommonConfig/Database/{id}")]
    public Task<ApiResponse<DatabaseConfigModel>> QueryDatabaseConfigAsync(string id)
    {
        return QueryConfigurationAsync<DatabaseConfigModel>(id);
    }

    [HttpPost("/api/CommonConfig/Database/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] DatabaseConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }

    [HttpGet("/api/CommonConfig/Database/VersionList")]
    public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryDatabaseConfigurationVersionListAsync(
    [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters);
    }

    [HttpPost("/api/CommonConfig/Database/SwitchVersion")]
    public Task<ApiResponse> SwitchDatabaseConfigurationVersionAsync(
        [FromBody] ConfigurationVersionSwitchParameters parameters)
    {
        return SwitchConfigurationVersionAsync<DatabaseConfigModel>(parameters);
    }

    [HttpPost("/api/CommonConfig/Database/DeleteVersion")]
    public Task<ApiResponse> DeleteDatabaseConfigurationVersionAsync(
    [FromBody] ConfigurationVersionRecordModel entity)
    {
        return DeleteConfigurationVersionAsync<DatabaseConfigModel>(new ConfigurationVersionDeleteParameters(entity));
    }
}