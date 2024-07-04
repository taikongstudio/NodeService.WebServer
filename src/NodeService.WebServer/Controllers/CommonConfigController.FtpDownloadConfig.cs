using NodeService.WebServer.Services.QueryOptimize;

namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/CommonConfig/ftpdownload/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] FtpDownloadConfigModel model)
    {
        model.FtpConfig = null;
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
        return QueryConfigurationAsync<FtpDownloadConfigModel>(id);
    }


    [HttpPost("/api/CommonConfig/ftpdownload/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] FtpDownloadConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }

    [HttpGet("/api/CommonConfig/ftpdownload/VersionList")]
    public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryFtpDownloadConfigurationVersionListAsync(
    [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters);
    }

    [HttpPost("/api/CommonConfig/ftpdownload/SwitchVersion")]
    public Task<ApiResponse> SwitchFtpDownloadConfigurationVersionAsync(
        [FromBody] ConfigurationVersionSwitchParameters parameters)
    {
        return SwitchConfigurationVersionAsync<FtpDownloadConfigModel>(parameters);
    }

    [HttpPost("/api/CommonConfig/ftpdownload/DeleteVersion")]
    public Task<ApiResponse> DeleteFtpDownloadConfigurationVersionAsync(
    [FromBody] ConfigurationVersionRecordModel entity)
    {
        return DeleteConfigurationVersionAsync<FtpDownloadConfigModel>(new ConfigurationVersionDeleteParameters(entity));
    }
}