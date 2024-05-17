namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/CommonConfig/kafka/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] KafkaConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model);
    }

    [HttpGet("/api/CommonConfig/kafka/List")]
    public Task<PaginationResponse<KafkaConfigModel>> QueryKafkaConfigurationListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<KafkaConfigModel>(queryParameters);
    }

    [HttpGet("/api/CommonConfig/kafka/{id}")]
    public Task<ApiResponse<KafkaConfigModel>> QueryKafkaConfigAsync(string id)
    {
        return QueryConfigurationAsync<KafkaConfigModel>(id);
    }


    [HttpPost("/api/CommonConfig/kafka/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] KafkaConfigModel kafkaConfig)
    {
        return DeleteConfigurationAsync(kafkaConfig);
    }
}