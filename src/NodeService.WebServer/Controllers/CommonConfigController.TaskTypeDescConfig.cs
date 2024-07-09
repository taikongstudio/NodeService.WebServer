using NodeService.WebServer.Services.DataQueue;

namespace NodeService.WebServer.Controllers;

public partial class ConfigurationController
{
    [HttpPost("/api/Configuration/TaskTypeDesc/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync(
        [FromBody] TaskTypeDescConfigModel model,
        CancellationToken cancellationToken)
    {
        return AddOrUpdateConfigurationAsync(model, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/Configuration/TaskTypeDesc/List")]
    public Task<PaginationResponse<TaskTypeDescConfigModel>> QueryTaskTypeDescConfigurationListAsync(
        [FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationListAsync<TaskTypeDescConfigModel>(queryParameters, cancellationToken);
    }


    [HttpPost("/api/Configuration/TaskTypeDesc/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] TaskTypeDescConfigModel model, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationAsync(model, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/Configuration/TaskTypeDesc/{id}")]
    public Task<ApiResponse<TaskTypeDescConfigModel>> QueryTaskTypeDescConfigAsync(string id, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationAsync<TaskTypeDescConfigModel>(id, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/Configuration/TaskTypeDesc/VersionList")]
    public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryTaskTypeDescVersionListAsync(
    [FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters, cancellationToken: cancellationToken);
    }

    [HttpPost("/api/Configuration/TaskTypeDesc/SwitchVersion")]
    public Task<ApiResponse> SwitchTaskTypeDescVersionAsync(
        [FromBody] ConfigurationVersionSwitchParameters parameters, CancellationToken cancellationToken = default)
    {
        return SwitchConfigurationVersionAsync<TaskTypeDescConfigModel>(parameters, cancellationToken: cancellationToken);
    }

    [HttpPost("/api/Configuration/TaskTypeDesc/DeleteVersion")]
    public Task<ApiResponse> DeleteTaskTypeDescVersionAsync(
    [FromBody] ConfigurationVersionRecordModel entity, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationVersionAsync<TaskTypeDescConfigModel>(new ConfigurationVersionDeleteParameters(entity), cancellationToken);
    }
}