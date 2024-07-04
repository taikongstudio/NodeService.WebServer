using NodeService.WebServer.Services.QueryOptimize;

namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/CommonConfig/ftpupload/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] FtpUploadConfigModel model)
    {
        model.FtpConfig = null;
        return AddOrUpdateConfigurationAsync(model);
    }

    [HttpGet("/api/CommonConfig/ftpupload/List")]
    public Task<PaginationResponse<FtpUploadConfigModel>> QueryFtpUploadConfigAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<FtpUploadConfigModel>(queryParameters);
    }

    [HttpGet("/api/CommonConfig/ftpupload/{id}")]
    public Task<ApiResponse<FtpUploadConfigModel>> QueryFtpUploadConfigAsync(string id)
    {
        return QueryConfigurationAsync<FtpUploadConfigModel>(id);
    }


    [HttpPost("/api/CommonConfig/ftpupload/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] FtpUploadConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }

    [HttpGet("/api/CommonConfig/ftpupload/VersionList")]
    public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryFtpUploadConfigurationVersionListAsync(
    [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters);
    }

    [HttpPost("/api/CommonConfig/ftpupload/SwitchVersion")]
    public Task<ApiResponse> SwitchFtpUploadConfigurationVersionAsync(
        [FromBody] ConfigurationVersionSwitchParameters parameters)
    {
        return SwitchConfigurationVersionAsync<FtpUploadConfigModel>(parameters);
    }

    [HttpPost("/api/CommonConfig/ftpupload/DeleteVersion")]
    public Task<ApiResponse> DeleteFtpUploadConfigurationVersionAsync(
        [FromBody] ConfigurationVersionRecordModel entity)
    {
        return DeleteConfigurationVersionAsync<FtpUploadConfigModel>(new ConfigurationVersionDeleteParameters(entity));
    }
}