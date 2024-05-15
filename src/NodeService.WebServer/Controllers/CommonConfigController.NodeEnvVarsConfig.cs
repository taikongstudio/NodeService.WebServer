namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/CommonConfig/nodeenvvars/addorupdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] NodeEnvVarsConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model);
    }

    [HttpGet("/api/CommonConfig/nodeenvvars/list")]
    public Task<PaginationResponse<NodeEnvVarsConfigModel>> QueryNodeEnvVarsListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<NodeEnvVarsConfigModel>(queryParameters);
    }

    [HttpGet("/api/CommonConfig/nodeenvvars/{id}")]
    public Task<ApiResponse<NodeEnvVarsConfigModel>> QueryNodeEnvVarsConfigAsync(string id)
    {
        return QueryConfigurationAsync<NodeEnvVarsConfigModel>(id);
    }

    [HttpPost("/api/CommonConfig/nodeenvvars/remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] NodeEnvVarsConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }
}