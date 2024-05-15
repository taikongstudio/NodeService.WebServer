namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/CommonConfig/mysql/addorupdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] MysqlConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model);
    }

    [HttpGet("/api/CommonConfig/mysql/list")]
    public Task<PaginationResponse<MysqlConfigModel>> QueryMysqlConfigurationListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<MysqlConfigModel>(queryParameters);
    }

    [HttpGet("/api/CommonConfig/mysql/{id}")]
    public Task<ApiResponse<MysqlConfigModel>> QueryMysqlConfigAsync(string id)
    {
        return QueryConfigurationAsync<MysqlConfigModel>(id);
    }

    [HttpPost("/api/CommonConfig/mysql/remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] MysqlConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }
}