using NodeService.WebServer.Services.QueryOptimize;

namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/CommonConfig/kafka/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] KafkaConfigModel model, CancellationToken cancellationToken = default)
    {
        return AddOrUpdateConfigurationAsync(model, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/CommonConfig/kafka/List")]
    public Task<PaginationResponse<KafkaConfigModel>> QueryKafkaConfigurationListAsync([FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationListAsync<KafkaConfigModel>(queryParameters, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/CommonConfig/kafka/{id}")]
    public Task<ApiResponse<KafkaConfigModel>> QueryKafkaConfigAsync(string id, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationAsync<KafkaConfigModel>(id, cancellationToken: cancellationToken);
    }


    [HttpPost("/api/CommonConfig/kafka/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] KafkaConfigModel kafkaConfig, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationAsync(kafkaConfig, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/CommonConfig/kafka/VersionList")]
    public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryKafkaConfigurationVersionListAsync([FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters, cancellationToken: cancellationToken);
    }

    [HttpPost("/api/CommonConfig/kafka/SwitchVersion")]
    public Task<ApiResponse> SwitchKafkaConfigurationVersionAsync([FromBody] ConfigurationVersionSwitchParameters parameters, CancellationToken cancellationToken = default)
    {
        return SwitchConfigurationVersionAsync<KafkaConfigModel>(parameters, cancellationToken: cancellationToken);
    }

    [HttpPost("/api/CommonConfig/kafka/DeleteVersion")]
    public Task<ApiResponse> DeleteKafkaConfigurationVersionAsync(
    [FromBody] ConfigurationVersionRecordModel entity, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationVersionAsync<KafkaConfigModel>(new ConfigurationVersionDeleteParameters(entity), cancellationToken);
    }
}