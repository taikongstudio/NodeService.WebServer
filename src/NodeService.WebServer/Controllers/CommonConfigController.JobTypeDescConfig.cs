namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/CommonConfig/jobtypedesc/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] JobTypeDescConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model);
    }

    [HttpGet("/api/CommonConfig/jobtypedesc/List")]
    public Task<PaginationResponse<JobTypeDescConfigModel>> QueryJobTypeDescConfigurationListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<JobTypeDescConfigModel>(queryParameters);
    }


    [HttpPost("/api/CommonConfig/jobtypedesc/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] JobTypeDescConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }

    [HttpGet("/api/CommonConfig/jobtypedesc/{id}")]
    public Task<ApiResponse<JobTypeDescConfigModel>> QueryJobTypeDescConfigAsync(string id)
    {
        return QueryConfigurationAsync<JobTypeDescConfigModel>(id);
    }
}