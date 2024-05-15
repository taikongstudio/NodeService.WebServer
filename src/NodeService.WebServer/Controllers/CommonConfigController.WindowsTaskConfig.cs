namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpGet("/api/CommonConfig/windowstask/list")]
    public Task<PaginationResponse<WindowsTaskConfigModel>> QueryWindowsTasksListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<WindowsTaskConfigModel>(queryParameters);
    }

    [HttpPost("/api/CommonConfig/windowstask/addorupdate")]
    public Task<ApiResponse> AddOrUpdateAsync(WindowsTaskConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model);
    }


    [HttpPost("/api/CommonConfig/windowstask/remove")]
    public Task<ApiResponse> RemoveAsync(WindowsTaskConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }
}