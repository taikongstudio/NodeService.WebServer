

using FluentFTP;

namespace NodeService.WebServer.Controllers
{
    public partial class CommonConfigController
    {


        [HttpPost("/api/commonconfig/ftp/addorupdate")]
        public Task<ApiResponse> AddOrUpdateAsync([FromBody] FtpConfigModel model)
        {
            return AddOrUpdateConfigurationAsync(model);
        }

        [HttpGet("/api/commonconfig/ftp/list")]
        public Task<PaginationResponse<FtpConfigModel>> QueryFtpConfigurationListAsync([FromQuery] QueryParametersModel queryParameters)
        {
            return QueryConfigurationListAsync<FtpConfigModel>(queryParameters);
        }

        [HttpGet("/api/commonconfig/ftp/{id}")]
        public Task<ApiResponse<FtpConfigModel>> QueryFtpConfigAsync(string id)
        {
            return QueryConfigurationAsync<FtpConfigModel>(id);
        }


        [HttpPost("/api/commonconfig/ftp/remove")]
        public Task<ApiResponse> RemoveAsync([FromBody] FtpConfigModel model)
        {
            return RemoveConfigurationAsync(model);
        }



    }
}
