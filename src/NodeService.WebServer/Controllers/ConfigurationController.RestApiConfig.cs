namespace NodeService.WebServer.Controllers;

public partial class ConfigurationController
{
    [HttpPost("/api/Configuration/restapi/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] RestApiConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model);
    }

    [HttpGet("/api/Configuration/restapi/List")]
    public Task<PaginationResponse<RestApiConfigModel>> QueryRestApiConfigurationListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<RestApiConfigModel>(queryParameters);
    }

    [HttpGet("/api/Configuration/restapi/{id}")]
    public Task<ApiResponse<RestApiConfigModel>> QueryRestApiConfigAsync(string id)
    {
        return QueryConfigurationAsync<RestApiConfigModel>(id);
    }


    [HttpPost("/api/Configuration/restapi/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] RestApiConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }
}