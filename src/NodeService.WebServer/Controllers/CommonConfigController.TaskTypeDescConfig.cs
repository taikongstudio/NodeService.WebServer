namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/CommonConfig/TaskTypeDesc/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] TaskTypeDescConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model);
    }

    [HttpGet("/api/CommonConfig/TaskTypeDesc/List")]
    public Task<PaginationResponse<TaskTypeDescConfigModel>> QueryJobTypeDescConfigurationListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<TaskTypeDescConfigModel>(queryParameters);
    }


    [HttpPost("/api/CommonConfig/TaskTypeDesc/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] TaskTypeDescConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }

    [HttpGet("/api/CommonConfig/TaskTypeDesc/{id}")]
    public Task<ApiResponse<TaskTypeDescConfigModel>> QueryJobTypeDescConfigAsync(string id)
    {
        return QueryConfigurationAsync<TaskTypeDescConfigModel>(id);
    }
}