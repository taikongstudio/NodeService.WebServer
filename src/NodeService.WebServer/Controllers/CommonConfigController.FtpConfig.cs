namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/CommonConfig/ftp/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] FtpConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model);
    }

    [HttpGet("/api/CommonConfig/ftp/List")]
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


    [HttpPost("/api/CommonConfig/ftp/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] FtpConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }
}