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
}