namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/commonconfig/ftpdownload/addorupdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] FtpDownloadConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model);
    }

    [HttpGet("/api/commonconfig/ftpdownload/list")]
    public Task<PaginationResponse<FtpDownloadConfigModel>> QueryFtpDownloadConfigAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<FtpDownloadConfigModel>(queryParameters);
    }

    [HttpGet("/api/commonconfig/ftpdownload/{id}")]
    public Task<ApiResponse<FtpDownloadConfigModel>> QueryFtpDownloadConfigAsync(string id)
    {
        return QueryConfigurationAsync<FtpDownloadConfigModel>(id, FindFtpConfigAsync);

        async Task FindFtpConfigAsync(FtpDownloadConfigModel? ftpDownloadConfig)
        {
            if (ftpDownloadConfig != null)
                ftpDownloadConfig.FtpConfig = (await QueryFtpConfigAsync(ftpDownloadConfig.FtpConfigId)).Result;
        }
    }


    [HttpPost("/api/commonconfig/ftpdownload/remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] FtpDownloadConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }
}