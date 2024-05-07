using Microsoft.AspNetCore.RateLimiting;

namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpGet("/api/commonconfig/windowstask/list")]
    public Task<PaginationResponse<WindowsTaskConfigModel>> QueryWindowsTasksListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<WindowsTaskConfigModel>(queryParameters);
    }

    [HttpPost("/api/commonconfig/windowstask/addorupdate")]
    public Task<ApiResponse> AddOrUpdateAsync(WindowsTaskConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model);
    }


    [HttpPost("/api/commonconfig/windowstask/remove")]
    public Task<ApiResponse> RemoveAsync(WindowsTaskConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }
}