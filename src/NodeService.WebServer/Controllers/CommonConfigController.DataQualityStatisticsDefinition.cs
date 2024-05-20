namespace NodeService.WebServer.Controllers
{
    public partial class CommonConfigController
    {
        [HttpPost("/api/CommonConfig/DataQualityStatisticsDefinition/AddOrUpdate")]
        public Task<ApiResponse> AddOrUpdateAsync([FromBody] DataQualityStatisticsDefinitionModel model)
        {
            return AddOrUpdateConfigurationAsync(model);
        }

        [HttpGet("/api/CommonConfig/DataQualityStatisticsDefinition/List")]
        public Task<PaginationResponse<DataQualityStatisticsDefinitionModel>> QueryDataQualityCounterDefinitionListAsync(
            [FromQuery] PaginationQueryParameters queryParameters)
        {
            return QueryConfigurationListAsync<DataQualityStatisticsDefinitionModel>(queryParameters);
        }

        [HttpGet("/api/CommonConfig/DataQualityStatisticsDefinition/{id}")]
        public Task<ApiResponse<DataQualityStatisticsDefinitionModel>> QueryDataQualityCounterDefinitionAsync(string id)
        {
            return QueryConfigurationAsync<DataQualityStatisticsDefinitionModel>(id);
        }


        [HttpPost("/api/CommonConfig/DataQualityStatisticsDefinition/Remove")]
        public Task<ApiResponse> RemoveAsync([FromBody] DataQualityStatisticsDefinitionModel model)
        {
            return DeleteConfigurationAsync(model);
        }
    }
}
