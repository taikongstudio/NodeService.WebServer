namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/commonconfig/ftpupload/addorupdate")]
    public Task<ApiResponse>
        AddOrUpdateAsync([FromBody] FtpUploadConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model);
    }

    [HttpGet("/api/commonconfig/ftpupload/list")]
    public Task<PaginationResponse<FtpUploadConfigModel>> QueryFtpUploadConfigAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<FtpUploadConfigModel>(queryParameters);
    }

    [HttpGet("/api/commonconfig/ftpupload/{id}")]
    public Task<ApiResponse<FtpUploadConfigModel>> QueryFtpUploadConfigAsync(string id)
    {
        return QueryConfigurationAsync<FtpUploadConfigModel>(id, FindFtpConfigAsync);

        async Task FindFtpConfigAsync(FtpUploadConfigModel? ftpUploadConfig)
        {
            if (ftpUploadConfig != null)
                ftpUploadConfig.FtpConfig = (await QueryFtpConfigAsync(ftpUploadConfig.FtpConfigId)).Result;
        }
    }


    [HttpPost("/api/commonconfig/ftpupload/remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] FtpUploadConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }
}