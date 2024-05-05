namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/commonconfig/kafka/addorupdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] KafkaConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model);
    }

    [HttpGet("/api/commonconfig/kafka/list")]
    public Task<PaginationResponse<KafkaConfigModel>> QueryKafkaConfigurationListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<KafkaConfigModel>(queryParameters);
    }

    [HttpGet("/api/commonconfig/kafka/{id}")]
    public Task<ApiResponse<KafkaConfigModel>> QueryKafkaConfigAsync(string id)
    {
        return QueryConfigurationAsync<KafkaConfigModel>(id);
    }


    [HttpPost("/api/commonconfig/kafka/remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] KafkaConfigModel kafkaConfig)
    {
        return DeleteConfigurationAsync(kafkaConfig);
    }
}