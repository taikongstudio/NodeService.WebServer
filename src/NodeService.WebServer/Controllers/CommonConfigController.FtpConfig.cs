using NodeService.WebServer.Services.QueryOptimize;

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

    [HttpGet("/api/CommonConfig/ftp/VersionList")]
    public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryFtpConfigurationVersionListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters);
    }

    [HttpPost("/api/CommonConfig/ftp/SwitchVersion")]
    public Task<ApiResponse> SwitchFtpConfigurationVersionAsync(
        [FromBody] ConfigurationVersionSwitchParameters parameters)
    {
        return SwitchConfigurationVersionAsync<FtpConfigModel>(parameters);
    }

    [HttpPost("/api/CommonConfig/ftp/DeleteVersion")]
    public Task<ApiResponse> DeleteFtpConfigurationVersionAsync(
    [FromBody]ConfigurationVersionRecordModel  entity)
    {
        return DeleteConfigurationVersionAsync<FtpConfigModel>(new ConfigurationVersionDeleteParameters(entity));
    }
}