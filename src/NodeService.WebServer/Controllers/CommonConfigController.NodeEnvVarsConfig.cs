using NodeService.WebServer.Services.QueryOptimize;

namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/CommonConfig/NodeEnvVars/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] NodeEnvVarsConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model);
    }

    [HttpGet("/api/CommonConfig/NodeEnvVars/List")]
    public Task<PaginationResponse<NodeEnvVarsConfigModel>> QueryNodeEnvVarListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<NodeEnvVarsConfigModel>(queryParameters);
    }

    [HttpGet("/api/CommonConfig/NodeEnvVars/{id}")]
    public Task<ApiResponse<NodeEnvVarsConfigModel>> QueryNodeEnvVarsConfigAsync(string id)
    {
        return QueryConfigurationAsync<NodeEnvVarsConfigModel>(id);
    }

    [HttpPost("/api/CommonConfig/NodeEnvVars/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] NodeEnvVarsConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }

    [HttpGet("/api/CommonConfig/NodeEnvVars/VersionList")]
    public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryNodeEnvVarsConfigurationVersionListAsync(
[FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters);
    }

    [HttpPost("/api/CommonConfig/NodeEnvVars/SwitchVersion")]
    public Task<ApiResponse> SwitchNodeEnvVarsConfigurationVersionAsync(
        [FromBody] ConfigurationVersionSwitchParameters parameters)
    {
        return SwitchConfigurationVersionAsync<NodeEnvVarsConfigModel>(parameters);
    }

    [HttpPost("/api/CommonConfig/NodeEnvVars/DeleteVersion")]
    public Task<ApiResponse> DeleteNodeEnvVarsConfigurationVersionAsync(
    [FromBody] ConfigurationVersionRecordModel entity)
    {
        return DeleteConfigurationVersionAsync<NodeEnvVarsConfigModel>(new ConfigurationVersionDeleteParameters(entity));
    }
}