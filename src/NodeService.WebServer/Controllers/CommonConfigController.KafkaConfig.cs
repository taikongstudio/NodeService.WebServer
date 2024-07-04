using NodeService.WebServer.Services.QueryOptimize;

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

    [HttpGet("/api/CommonConfig/kafka/VersionList")]
    public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryKafkaConfigurationVersionListAsync(
    [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters);
    }

    [HttpPost("/api/CommonConfig/kafka/SwitchVersion")]
    public Task<ApiResponse> SwitchKafkaConfigurationVersionAsync(
        [FromBody] ConfigurationVersionSwitchParameters parameters)
    {
        return SwitchConfigurationVersionAsync<KafkaConfigModel>(parameters);
    }

    [HttpPost("/api/CommonConfig/kafka/DeleteVersion")]
    public Task<ApiResponse> DeleteKafkaConfigurationVersionAsync(
    [FromBody] ConfigurationVersionRecordModel entity)
    {
        return DeleteConfigurationVersionAsync<KafkaConfigModel>(new ConfigurationVersionDeleteParameters(entity));
    }
}