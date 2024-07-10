using NodeService.WebServer.Services.DataQueue;

namespace NodeService.WebServer.Controllers;

public partial class ConfigurationController
{
    [HttpPost("/api/CommonConfig/FtpDownload/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] FtpDownloadConfigModel model)
    {
        model.FtpConfig = null;
        return AddOrUpdateConfigurationAsync(model);
    }

    [HttpGet("/api/CommonConfig/FtpDownload/List")]
    public Task<PaginationResponse<FtpDownloadConfigModel>> QueryFtpDownloadConfigAsync(
        [FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationListAsync<FtpDownloadConfigModel>(queryParameters, cancellationToken);
    }

    [HttpGet("/api/CommonConfig/FtpDownload/{id}")]
    public Task<ApiResponse<FtpDownloadConfigModel>> QueryFtpDownloadConfigAsync(string id, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationAsync<FtpDownloadConfigModel>(id, QueryFtpConfigAsync, cancellationToken: cancellationToken);
    }

    async ValueTask QueryFtpConfigAsync(FtpDownloadConfigModel? ftpDownloadConfig, CancellationToken cancellationToken = default)
    {
        if (ftpDownloadConfig == null)
        {
            return;
        }
        var rsp = await QueryConfigurationAsync<FtpConfigModel>(ftpDownloadConfig.Value.FtpConfigId, null, cancellationToken);
        if (rsp.ErrorCode == 0)
        {
            ftpDownloadConfig.Value.FtpConfig = rsp.Result;
        }
    }


    [HttpPost("/api/CommonConfig/FtpDownload/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] FtpDownloadConfigModel model, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationAsync(model, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/CommonConfig/FtpDownload/VersionList")]
    public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryFtpDownloadConfigurationVersionListAsync(
    [FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters, cancellationToken);
    }

    [HttpPost("/api/CommonConfig/FtpDownload/SwitchVersion")]
    public Task<ApiResponse> SwitchFtpDownloadConfigurationVersionAsync(
        [FromBody] ConfigurationVersionSwitchParameters parameters,CancellationToken  cancellationToken=default)
    {
        return SwitchConfigurationVersionAsync<FtpDownloadConfigModel>(parameters, cancellationToken: cancellationToken);
    }

    [HttpPost("/api/CommonConfig/FtpDownload/DeleteVersion")]
    public Task<ApiResponse> DeleteFtpDownloadConfigurationVersionAsync(
    [FromBody] ConfigurationVersionRecordModel entity, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationVersionAsync<FtpDownloadConfigModel>(new ConfigurationVersionDeleteParameters(entity), cancellationToken);
    }
}