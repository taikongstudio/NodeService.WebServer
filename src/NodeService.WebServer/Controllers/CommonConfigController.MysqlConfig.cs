

namespace NodeService.WebServer.Controllers
{
    public partial class CommonConfigController
    {


        [HttpPost("/api/commonconfig/mysql/addorupdate")]
        public Task<ApiResponse> AddOrUpdateAsync([FromBody] MysqlConfigModel model)
        {
            return AddOrUpdateConfigurationAsync(model);
        }

        [HttpGet("/api/commonconfig/mysql/list")]
        public Task<PaginationResponse<MysqlConfigModel>> QueryMysqlConfigurationListAsync([FromQuery] QueryParametersModel queryParameters)
        {
            return QueryConfigurationListAsync<MysqlConfigModel>(queryParameters);
        }

        [HttpGet("/api/commonconfig/mysql/{id}")]
        public Task<ApiResponse<MysqlConfigModel>> QueryMysqlConfigAsync(string id)
        {
            return QueryConfigurationAsync<MysqlConfigModel>(id);
        }

        [HttpPost("/api/commonconfig/mysql/remove")]
        public Task<ApiResponse> RemoveAsync([FromBody] MysqlConfigModel model)
        {
            return DeleteConfigurationAsync(model);
        }



    }
}
