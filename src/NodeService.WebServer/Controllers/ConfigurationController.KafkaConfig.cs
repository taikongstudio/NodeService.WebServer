using NodeService.WebServer.Services.DataQueue;

namespace NodeService.WebServer.Controllers;

public partial class ConfigurationController
{
    [HttpPost("/api/Configuration/kafka/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] KafkaConfigModel model, CancellationToken cancellationToken = default)
    {
        return AddOrUpdateConfigurationAsync(model, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/Configuration/kafka/List")]
    public Task<PaginationResponse<KafkaConfigModel>> QueryKafkaConfigurationListAsync([FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationListAsync<KafkaConfigModel>(queryParameters, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/Configuration/kafka/{id}")]
    public Task<ApiResponse<KafkaConfigModel>> QueryKafkaConfigAsync(string id, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationAsync<KafkaConfigModel>(id, cancellationToken: cancellationToken);
    }


    [HttpPost("/api/Configuration/kafka/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] KafkaConfigModel kafkaConfig, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationAsync(kafkaConfig, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/Configuration/kafka/VersionList")]
    public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryKafkaConfigurationVersionListAsync([FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters, cancellationToken: cancellationToken);
    }

    [HttpPost("/api/Configuration/kafka/SwitchVersion")]
    public Task<ApiResponse> SwitchKafkaConfigurationVersionAsync([FromBody] ConfigurationVersionSwitchParameters parameters, CancellationToken cancellationToken = default)
    {
        return SwitchConfigurationVersionAsync<KafkaConfigModel>(parameters, cancellationToken: cancellationToken);
    }

    [HttpPost("/api/Configuration/kafka/DeleteVersion")]
    public Task<ApiResponse> DeleteKafkaConfigurationVersionAsync(
    [FromBody] ConfigurationVersionRecordModel entity, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationVersionAsync<KafkaConfigModel>(new ConfigurationVersionDeleteParameters(entity), cancellationToken);
    }
}