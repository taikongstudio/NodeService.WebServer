namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/CommonConfig/ftpupload/addorupdate")]
    public Task<ApiResponse>
        AddOrUpdateAsync([FromBody] FtpUploadConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model);
    }

    [HttpGet("/api/CommonConfig/ftpupload/list")]
    public Task<PaginationResponse<FtpUploadConfigModel>> QueryFtpUploadConfigAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<FtpUploadConfigModel>(queryParameters);
    }

    [HttpGet("/api/CommonConfig/ftpupload/{id}")]
    public Task<ApiResponse<FtpUploadConfigModel>> QueryFtpUploadConfigAsync(string id)
    {
        return QueryConfigurationAsync<FtpUploadConfigModel>(id, FindFtpConfigAsync);

        async Task FindFtpConfigAsync(FtpUploadConfigModel? ftpUploadConfig)
        {
            if (ftpUploadConfig != null)
                ftpUploadConfig.FtpConfig = (await QueryFtpConfigAsync(ftpUploadConfig.FtpConfigId)).Result;
        }
    }


    [HttpPost("/api/CommonConfig/ftpupload/remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] FtpUploadConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }
}