using NodeService.WebServer.Services.DataServices;

namespace NodeService.WebServer.Controllers;

public partial class ConfigurationController
{
    [HttpPost("/api/CommonConfig/Ftp/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] FtpConfigModel model, CancellationToken cancellationToken = default)
    {
        return AddOrUpdateConfigurationAsync(model, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/CommonConfig/Ftp/List")]
    public Task<PaginationResponse<FtpConfigModel>> QueryFtpConfigurationListAsync([FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationListAsync<FtpConfigModel>(queryParameters, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/CommonConfig/Ftp/{id}")]
    public Task<ApiResponse<FtpConfigModel>> QueryFtpConfigAsync(string id, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationAsync<FtpConfigModel>(id, cancellationToken: cancellationToken);
    }


    [HttpPost("/api/CommonConfig/Ftp/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] FtpConfigModel model, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationAsync(model, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/CommonConfig/Ftp/VersionList")]
    public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryFtpConfigurationVersionListAsync([FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters, cancellationToken: cancellationToken);
    }

    [HttpPost("/api/CommonConfig/Ftp/SwitchVersion")]
    public Task<ApiResponse> SwitchFtpConfigurationVersionAsync([FromBody] ConfigurationVersionSwitchParameters parameters, CancellationToken cancellationToken = default)
    {
        return SwitchConfigurationVersionAsync<FtpConfigModel>(parameters, cancellationToken: cancellationToken);
    }

    [HttpPost("/api/CommonConfig/Ftp/DeleteVersion")]
    public Task<ApiResponse> DeleteFtpConfigurationVersionAsync(
    [FromBody] ConfigurationVersionRecordModel entity, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationVersionAsync<FtpConfigModel>(new ConfigurationVersionDeleteParameters(entity), cancellationToken);
    }
}