namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/commonconfig/jobtypedesc/addorupdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] JobTypeDescConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model);
    }

    [HttpGet("/api/commonconfig/jobtypedesc/list")]
    public Task<PaginationResponse<JobTypeDescConfigModel>> QueryJobTypeDescConfigurationListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<JobTypeDescConfigModel>(queryParameters);
    }


    [HttpPost("/api/commonconfig/jobtypedesc/remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] JobTypeDescConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }

    [HttpGet("/api/commonconfig/jobtypedesc/{id}")]
    public Task<ApiResponse<JobTypeDescConfigModel>> QueryJobTypeDescConfigAsync(string id)
    {
        return QueryConfigurationAsync<JobTypeDescConfigModel>(id);
    }
}