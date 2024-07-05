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
        [FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationListAsync<FtpDownloadConfigModel>(queryParameters, cancellationToken);
    }

    [HttpGet("/api/CommonConfig/ftpdownload/{id}")]
    public Task<ApiResponse<FtpDownloadConfigModel>> QueryFtpDownloadConfigAsync(string id, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationAsync<FtpDownloadConfigModel>(id, cancellationToken: cancellationToken);
    }


    [HttpPost("/api/CommonConfig/ftpdownload/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] FtpDownloadConfigModel model, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationAsync(model, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/CommonConfig/ftpdownload/VersionList")]
    public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryFtpDownloadConfigurationVersionListAsync(
    [FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters, cancellationToken);
    }

    [HttpPost("/api/CommonConfig/ftpdownload/SwitchVersion")]
    public Task<ApiResponse> SwitchFtpDownloadConfigurationVersionAsync(
        [FromBody] ConfigurationVersionSwitchParameters parameters,CancellationToken  cancellationToken=default)
    {
        return SwitchConfigurationVersionAsync<FtpDownloadConfigModel>(parameters, cancellationToken: cancellationToken);
    }

    [HttpPost("/api/CommonConfig/ftpdownload/DeleteVersion")]
    public Task<ApiResponse> DeleteFtpDownloadConfigurationVersionAsync(
    [FromBody] ConfigurationVersionRecordModel entity, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationVersionAsync<FtpDownloadConfigModel>(new ConfigurationVersionDeleteParameters(entity), cancellationToken);
    }
}