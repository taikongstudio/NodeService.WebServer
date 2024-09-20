namespace NodeService.WebServer.Controllers;

public partial class ConfigurationController
{
    [HttpPost("/api/CommonConfig/RestApi/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] RestApiConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model);
    }

    [HttpGet("/api/CommonConfig/RestApi/List")]
    public Task<PaginationResponse<RestApiConfigModel>> QueryRestApiConfigurationListAsync(
        [FromQuery] PaginationQueryParameters queryParameters,
        CancellationToken cancellationToken = default)
    {
        return QueryConfigurationListAsync<RestApiConfigModel>(queryParameters, cancellationToken);
    }

    [HttpGet("/api/CommonConfig/RestApi/{id}")]
    public Task<ApiResponse<RestApiConfigModel>> QueryRestApiConfigAsync(string id)
    {
        return QueryConfigurationAsync<RestApiConfigModel>(id);
    }


    [HttpPost("/api/CommonConfig/RestApi/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] RestApiConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }
}