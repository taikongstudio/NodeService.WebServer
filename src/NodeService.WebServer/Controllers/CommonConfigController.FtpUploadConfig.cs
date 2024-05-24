﻿namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/CommonConfig/ftpupload/AddOrUpdate")]
    public Task<ApiResponse>
        AddOrUpdateAsync([FromBody] FtpUploadConfigModel model)
    {
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

        async Task FindFtpConfigAsync(FtpUploadConfigModel? ftpUploadConfig)
        {
            if (ftpUploadConfig != null)
                ftpUploadConfig.FtpConfig = (await QueryFtpConfigAsync(ftpUploadConfig.FtpConfigId)).Result;
        }
    }


    [HttpPost("/api/CommonConfig/ftpupload/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] FtpUploadConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }
}