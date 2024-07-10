using NodeService.WebServer.Services.DataQueue;

namespace NodeService.WebServer.Controllers;

public partial class ConfigurationController
{
    [HttpPost("/api/CommonConfig/DataQualityStatisticsDefinition/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] DataQualityStatisticsDefinitionModel model, CancellationToken cancellationToken = default)
    {
        return AddOrUpdateConfigurationAsync(model, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/CommonConfig/DataQualityStatisticsDefinition/List")]
    public Task<PaginationResponse<DataQualityStatisticsDefinitionModel>> QueryDataQualityCounterDefinitionListAsync(
        [FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationListAsync<DataQualityStatisticsDefinitionModel>(queryParameters, cancellationToken);
    }

    [HttpGet("/api/CommonConfig/DataQualityStatisticsDefinition/{id}")]
    public Task<ApiResponse<DataQualityStatisticsDefinitionModel>> QueryDataQualityCounterDefinitionAsync(string id, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationAsync<DataQualityStatisticsDefinitionModel>(id, cancellationToken: cancellationToken);
    }


    [HttpPost("/api/CommonConfig/DataQualityStatisticsDefinition/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] DataQualityStatisticsDefinitionModel model, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationAsync(model, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/CommonConfig/DataQualityStatisticsDefinition/VersionList")]
    public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryDataQualityDefinitionVersionListAsync(
        [FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters, cancellationToken);
    }

    [HttpPost("/api/CommonConfig/DataQualityStatisticsDefinition/SwitchVersion")]
    public Task<ApiResponse> SwitchDataQualityDefinitionVersionAsync(
        [FromBody] ConfigurationVersionSwitchParameters parameters, CancellationToken cancellationToken = default)
    {
        return SwitchConfigurationVersionAsync<DataQualityStatisticsDefinitionModel>(parameters, cancellationToken: cancellationToken);
    }

    [HttpPost("/api/CommonConfig/DataQualityStatisticsDefinition/DeleteVersion")]
    public Task<ApiResponse> DeleteDataQualityStatisticsDefinitionVersionAsync(
        [FromBody] ConfigurationVersionRecordModel entity, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationVersionAsync<DataQualityStatisticsDefinitionModel>(new ConfigurationVersionDeleteParameters(entity), cancellationToken);
    }
}