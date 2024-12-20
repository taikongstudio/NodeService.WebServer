﻿using NodeService.WebServer.Services.DataServices;

namespace NodeService.WebServer.Controllers;

public partial class ConfigurationController
{
    [HttpPost("/api/CommonConfig/NodeEnvVars/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] NodeEnvVarsConfigModel model, CancellationToken cancellationToken = default)
    {
        return AddOrUpdateConfigurationAsync(model, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/CommonConfig/NodeEnvVars/List")]
    public Task<PaginationResponse<NodeEnvVarsConfigModel>> QueryNodeEnvVarListAsync([FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationListAsync<NodeEnvVarsConfigModel>(queryParameters, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/CommonConfig/NodeEnvVars/{id}")]
    public Task<ApiResponse<NodeEnvVarsConfigModel>> QueryNodeEnvVarsConfigAsync(string id, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationAsync<NodeEnvVarsConfigModel>(id, cancellationToken: cancellationToken);
    }

    [HttpPost("/api/CommonConfig/NodeEnvVars/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] NodeEnvVarsConfigModel model, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationAsync(model, cancellationToken: cancellationToken);
    }

    [HttpGet("/api/CommonConfig/NodeEnvVars/VersionList")]
    public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryNodeEnvVarsConfigurationVersionListAsync([FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters, cancellationToken: cancellationToken);
    }

    [HttpPost("/api/CommonConfig/NodeEnvVars/SwitchVersion")]
    public Task<ApiResponse> SwitchNodeEnvVarsConfigurationVersionAsync([FromBody] ConfigurationVersionSwitchParameters parameters, CancellationToken cancellationToken = default)
    {
        return SwitchConfigurationVersionAsync<NodeEnvVarsConfigModel>(parameters, cancellationToken: cancellationToken);
    }

    [HttpPost("/api/CommonConfig/NodeEnvVars/DeleteVersion")]
    public Task<ApiResponse> DeleteNodeEnvVarsConfigurationVersionAsync(
    [FromBody] ConfigurationVersionRecordModel entity, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationVersionAsync<NodeEnvVarsConfigModel>(new ConfigurationVersionDeleteParameters(entity), cancellationToken);
    }
}