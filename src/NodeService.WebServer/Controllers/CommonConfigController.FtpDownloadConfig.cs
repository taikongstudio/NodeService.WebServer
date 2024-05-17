namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/CommonConfig/ftpdownload/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] FtpDownloadConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model);
    }

    [HttpGet("/api/CommonConfig/ftpdownload/List")]
    public Task<PaginationResponse<FtpDownloadConfigModel>> QueryFtpDownloadConfigAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<FtpDownloadConfigModel>(queryParameters);
    }

    [HttpGet("/api/CommonConfig/ftpdownload/{id}")]
    public Task<ApiResponse<FtpDownloadConfigModel>> QueryFtpDownloadConfigAsync(string id)
    {
        return QueryConfigurationAsync<FtpDownloadConfigModel>(id, FindFtpConfigAsync);

        async Task FindFtpConfigAsync(FtpDownloadConfigModel? ftpDownloadConfig)
        {
            if (ftpDownloadConfig != null)
                ftpDownloadConfig.FtpConfig = (await QueryFtpConfigAsync(ftpDownloadConfig.FtpConfigId)).Result;
        }
    }


    [HttpPost("/api/CommonConfig/ftpdownload/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] FtpDownloadConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }
}