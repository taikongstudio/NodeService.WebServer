namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/CommonConfig/restapi/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] RestApiConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model);
    }

    [HttpGet("/api/CommonConfig/restapi/List")]
    public Task<PaginationResponse<RestApiConfigModel>> QueryRestApiConfigurationListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<RestApiConfigModel>(queryParameters);
    }

    [HttpGet("/api/CommonConfig/restapi/{id}")]
    public Task<ApiResponse<RestApiConfigModel>> QueryRestApiConfigAsync(string id)
    {
        return QueryConfigurationAsync<RestApiConfigModel>(id);
    }


    [HttpPost("/api/CommonConfig/restapi/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] RestApiConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }
}