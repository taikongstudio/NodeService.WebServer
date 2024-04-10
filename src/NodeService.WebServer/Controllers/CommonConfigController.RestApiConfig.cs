

namespace NodeService.WebServer.Controllers
{
    public partial class CommonConfigController
    {


        [HttpPost("/api/commonconfig/restapi/addorupdate")]
        public Task<ApiResponse> AddOrUpdateAsync([FromBody] RestApiConfigModel model)
        {
            return AddOrUpdateConfigurationAsync(model);
        }

        [HttpGet("/api/commonconfig/restapi/list")]
        public Task<PaginationResponse<RestApiConfigModel>> QueryRestApiConfigurationListAsync([FromQuery] QueryParametersModel queryParameters)
        {
            return QueryConfigurationListAsync<RestApiConfigModel>(queryParameters);
        }

        [HttpGet("/api/commonconfig/restapi/{id}")]
        public Task<ApiResponse<RestApiConfigModel>> QueryRestApiConfigAsync(string id)
        {
            return QueryConfigurationAsync<RestApiConfigModel>(id);
        }


        [HttpPost("/api/commonconfig/restapi/remove")]
        public Task<ApiResponse> RemoveAsync([FromBody] RestApiConfigModel model)
        {
            return RemoveConfigurationAsync(model);
        }


    }
}
