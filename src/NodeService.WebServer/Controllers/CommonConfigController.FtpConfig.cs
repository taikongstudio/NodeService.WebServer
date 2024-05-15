namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/CommonConfig/ftp/addorupdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] FtpConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model);
    }

    [HttpGet("/api/CommonConfig/ftp/list")]
    public Task<PaginationResponse<FtpConfigModel>> QueryFtpConfigurationListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<FtpConfigModel>(queryParameters);
    }

    [HttpGet("/api/CommonConfig/ftp/{id}")]
    public Task<ApiResponse<FtpConfigModel>> QueryFtpConfigAsync(string id)
    {
        return QueryConfigurationAsync<FtpConfigModel>(id);
    }


    [HttpPost("/api/CommonConfig/ftp/remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] FtpConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }
}