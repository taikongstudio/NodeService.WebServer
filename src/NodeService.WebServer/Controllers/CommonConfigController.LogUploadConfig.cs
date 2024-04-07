

namespace NodeService.WebServer.Controllers
{
    public partial class CommonConfigController
    {


        [HttpPost("/api/commonconfig/logupload/addorupdate")]
        public Task<ApiResponse> AddOrUploadAsync([FromBody] LogUploadConfigModel model)
        {
            return AddOrUpdateConfigurationAsync(model);
        }

        [HttpGet("/api/commonconfig/logupload/list")]
        public  Task<PaginationResponse<LogUploadConfigModel>> QueryLogUploadConfigurationListAsync([FromQuery] QueryParametersModel queryParameters)
        {
            return QueryConfigurationListAsync<LogUploadConfigModel>(queryParameters);
        }

        [HttpGet("/api/commonconfig/logupload/{id}")]
        public Task<ApiResponse<LogUploadConfigModel>> QueryLogUploadConfigAsync(string id)
        {
            return QueryConfigurationAsync<LogUploadConfigModel>(id, FindFtpConfigAsync);
 
            async Task FindFtpConfigAsync(LogUploadConfigModel?  logUploadConfig)
            {
                if (logUploadConfig != null)
                {
                    logUploadConfig.FtpConfig = (await this.QueryFtpConfigAsync(logUploadConfig.FtpConfigId)).Result;
                }
            }
        }


        [HttpPost("/api/commonconfig/logupload/remove")]
        public Task<ApiResponse> RemoveAsync([FromBody] LogUploadConfigModel model)
        {
            return RemoveConfigurationAsync(model);
        }



    }
}
