namespace NodeService.WebServer.Controllers
{
    public partial class CommonConfigController
    {
        [HttpPost("/api/CommonConfig/TaskFlowTemplate/AddOrUpdate")]
        public Task<ApiResponse> AddOrUpdateAsync([FromBody] TaskFlowTemplateModel model)
        {
            return AddOrUpdateConfigurationAsync(model);
        }

        [HttpGet("/api/CommonConfig/TaskFlowTemplate/List")]
        public Task<PaginationResponse<TaskFlowTemplateModel>> QueryTaskFlowTemplateListAsync(
            [FromQuery] PaginationQueryParameters queryParameters)
        {
            return QueryConfigurationListAsync<TaskFlowTemplateModel>(queryParameters);
        }

        [HttpGet("/api/CommonConfig/TaskFlowTemplate/{id}")]
        public Task<ApiResponse<TaskFlowTemplateModel>> QueryTaskFlowTemplateAsync(string id)
        {
            return QueryConfigurationAsync<TaskFlowTemplateModel>(id);
        }


        [HttpPost("/api/CommonConfig/TaskFlowTemplate/Remove")]
        public Task<ApiResponse> RemoveAsync([FromBody] TaskFlowTemplateModel model)
        {
            return DeleteConfigurationAsync(model);
        }

        [HttpGet("/api/CommonConfig/TaskFlowTemplate/VersionList")]
        public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryTaskFlowTemplateConfigurationVersionListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
        {
            return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters);
        }

        [HttpPost("/api/CommonConfig/TaskFlowTemplate/SwitchVersion")]
        public Task<ApiResponse> SwitchTaskFlowTemplateConfigurationVersionAsync(
            [FromBody] ConfigurationVersionSwitchParameters parameters)
        {
            return SwitchConfigurationVersionAsync<TaskFlowTemplateModel>(parameters);
        }

        [HttpPost("/api/CommonConfig/TaskFlowTemplate/DeleteVersion")]
        public Task<ApiResponse> DeleteTaskFlowTemplateConfigurationVersionAsync(
            [FromBody] ConfigurationVersionRecordModel entity)
        {
            return DeleteConfigurationVersionAsync<TaskFlowTemplateModel>(new ConfigurationVersionDeleteParameters(entity));
        }



    }
}
