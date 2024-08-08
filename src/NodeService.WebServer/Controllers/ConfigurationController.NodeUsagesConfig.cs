using NodeService.WebServer.Services.DataServices;

namespace NodeService.WebServer.Controllers;

public partial class ConfigurationController
{
    [HttpPost("/api/CommonConfig/NodeUsage/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] NodeUsageConfigurationModel model, CancellationToken cancellationToken = default)
    {
        return AddOrUpdateConfigurationAsync(model, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/CommonConfig/NodeUsage/List")]
    public Task<PaginationResponse<NodeUsageConfigurationModel>> QueryNodeUsagesConfigurationListAsync([FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationListAsync<NodeUsageConfigurationModel>(queryParameters, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/CommonConfig/NodeUsage/{id}")]
    public Task<ApiResponse<NodeUsageConfigurationModel>> QueryNodeUsagesConfigAsync(string id, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationAsync<NodeUsageConfigurationModel>(id, cancellationToken: cancellationToken);
    }


    [HttpPost("/api/CommonConfig/NodeUsage/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] NodeUsageConfigurationModel kafkaConfig, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationAsync(kafkaConfig, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/CommonConfig/NodeUsage/VersionList")]
    public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryNodeUsagesConfigurationVersionListAsync([FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters, cancellationToken: cancellationToken);
    }

    [HttpPost("/api/CommonConfig/NodeUsage/SwitchVersion")]
    public Task<ApiResponse> SwitchNodeUsagesConfigurationVersionAsync([FromBody] ConfigurationVersionSwitchParameters parameters, CancellationToken cancellationToken = default)
    {
        return SwitchConfigurationVersionAsync<NodeUsageConfigurationModel>(parameters, cancellationToken: cancellationToken);
    }

    [HttpPost("/api/CommonConfig/NodeUsage/DeleteVersion")]
    public Task<ApiResponse> DeleteNodeUsgaesConfigurationVersionAsync(
    [FromBody] ConfigurationVersionRecordModel entity, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationVersionAsync<NodeUsageConfigurationModel>(new ConfigurationVersionDeleteParameters(entity), cancellationToken);
    }
}