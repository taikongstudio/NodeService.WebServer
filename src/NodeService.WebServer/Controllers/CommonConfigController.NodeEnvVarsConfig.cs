

namespace NodeService.WebServer.Controllers
{
    public partial class CommonConfigController
    {


        [HttpPost("/api/commonconfig/nodeenvvars/addorupdate")]
        public Task<ApiResponse> AddOrUpdateAsync([FromBody] NodeEnvVarsConfigModel model)
        {
            return AddOrUpdateConfigurationAsync(model);
        }

        [HttpGet("/api/commonconfig/nodeenvvars/list")]
        public Task<PaginationResponse<NodeEnvVarsConfigModel>> QueryNodeEnvVarsListAsync([FromQuery] QueryParametersModel queryParameters)
        {
            return QueryConfigurationListAsync<NodeEnvVarsConfigModel>(queryParameters);
        }

        [HttpGet("/api/commonconfig/nodeenvvars/{id}")]
        public Task<ApiResponse<NodeEnvVarsConfigModel>> QueryNodeEnvVarsConfigAsync(string id)
        {
            return QueryConfigurationAsync<NodeEnvVarsConfigModel>(id);
        }

        [HttpPost("/api/commonconfig/nodeenvvars/remove")]
        public Task<ApiResponse> RemoveAsync([FromBody] NodeEnvVarsConfigModel model)
        {
            return RemoveConfigurationAsync(model);
        }



    }
}
