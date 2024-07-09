using NodeService.WebServer.Services.DataQueue;

namespace NodeService.WebServer.Controllers;

public partial class ConfigurationController
{
    [HttpPost("/api/Configuration/ftpupload/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] FtpUploadConfigModel model,CancellationToken cancellationToken=default)
    {
        model.FtpConfig = null;
        return AddOrUpdateConfigurationAsync(model, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/Configuration/ftpupload/List")]
    public Task<PaginationResponse<FtpUploadConfigModel>> QueryFtpUploadConfigAsync(
        [FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationListAsync<FtpUploadConfigModel>(queryParameters, cancellationToken);
    }

    [HttpGet("/api/Configuration/ftpupload/{id}")]
    public Task<ApiResponse<FtpUploadConfigModel>> QueryFtpUploadConfigAsync(string id, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationAsync<FtpUploadConfigModel>(id, QueryFtpConfigAsync, cancellationToken: cancellationToken);
    }

    async ValueTask QueryFtpConfigAsync(FtpUploadConfigModel? ftpUploadConfig, CancellationToken cancellationToken = default)
    {
        if (ftpUploadConfig == null)
        {
            return;
        }
        var rsp = await QueryConfigurationAsync<FtpConfigModel>(ftpUploadConfig.Value.FtpConfigId, null, cancellationToken);
        if (rsp.ErrorCode == 0)
        {
            ftpUploadConfig.Value.FtpConfig = rsp.Result;
        }
    }

    [HttpPost("/api/Configuration/ftpupload/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] FtpUploadConfigModel model, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationAsync(model, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/Configuration/ftpupload/VersionList")]
    public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryFtpUploadConfigurationVersionListAsync(
    [FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters, cancellationToken: cancellationToken);
    }

    [HttpPost("/api/Configuration/ftpupload/SwitchVersion")]
    public Task<ApiResponse> SwitchFtpUploadConfigurationVersionAsync(
        [FromBody] ConfigurationVersionSwitchParameters parameters, CancellationToken cancellationToken = default)
    {
        return SwitchConfigurationVersionAsync<FtpUploadConfigModel>(parameters, cancellationToken: cancellationToken);
    }

    [HttpPost("/api/Configuration/ftpupload/DeleteVersion")]
    public Task<ApiResponse> DeleteFtpUploadConfigurationVersionAsync(
        [FromBody] ConfigurationVersionRecordModel entity, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationVersionAsync<FtpUploadConfigModel>(new ConfigurationVersionDeleteParameters(entity), cancellationToken);
    }
}