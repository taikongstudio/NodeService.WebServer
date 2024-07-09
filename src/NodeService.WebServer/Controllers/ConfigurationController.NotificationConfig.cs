using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.DataQueue;

namespace NodeService.WebServer.Controllers;

public partial class ConfigurationController
{
    [HttpPost("/api/Configuration/Notification/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] NotificationConfigModel model, CancellationToken cancellationToken = default)
    {
        return AddOrUpdateConfigurationAsync(model);
    }

    [HttpGet("/api/Configuration/Notification/List")]
    public Task<PaginationResponse<NotificationConfigModel>> QueryNotificationConfigurationListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<NotificationConfigModel>(queryParameters);
    }

    [HttpGet("/api/Configuration/Notification/VersionList")]
    public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryNotificationConfigurationVersionListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters);
    }

    [HttpPost("/api/Configuration/Notification/SwitchVersion")]
    public Task<ApiResponse> SwitchNotificationVersionAsync(
        [FromBody] ConfigurationVersionSwitchParameters parameters, CancellationToken cancellationToken = default)
    {
        return SwitchConfigurationVersionAsync<NotificationConfigModel>(parameters);
    }

    [HttpPost("/api/Configuration/Notification/DeleteVersion")]
    public Task<ApiResponse> DeleteNotificationConfigurationVersionAsync(
    [FromBody] ConfigurationVersionRecordModel entity, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationVersionAsync<NotificationConfigModel>(new ConfigurationVersionDeleteParameters(entity));
    }

    [HttpGet("/api/Configuration/Notification/{id}")]
    public Task<ApiResponse<NotificationConfigModel>> QueryNotificationConfigAsync(string id)
    {
        return QueryConfigurationAsync<NotificationConfigModel>(id);
    }

    [HttpPost("/api/Configuration/Notification/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] NotificationConfigModel model, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationAsync(model);
    }

    [HttpPost("/api/Configuration/Notification/{id}/Invoke")]
    public async Task<ApiResponse> InvokeAsync(string id, [FromBody] InvokeNotificationParameters parameters)
    {
        var rsp = await QueryNotificationConfigAsync(id);
        if (rsp.ErrorCode != 0) return rsp;
        try
        {
            await _notificationMessageQueue.EnqueueAsync(new NotificationMessage(parameters.Subject, parameters.Message,
                rsp.Result.Value));
        }
        catch (Exception ex)
        {
            rsp.ErrorCode = ex.HResult;
            rsp.Message = ex.ToString();
        }

        return rsp;
    }

    [HttpGet("/api/Configuration/NotificationSource/NodeHealthyCheck")]
    public async Task<ApiResponse<NodeHealthyCheckConfiguration>> QueryNodeHealthyCheckConfigurationAsync()
    {
        var rsp = new ApiResponse<NodeHealthyCheckConfiguration>();
        try
        {
            NodeHealthyCheckConfiguration? result = null;
            var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<PropertyBag>>();
            using var repo = repoFactory.CreateRepository();
            var propertyBag =
                await repo.FirstOrDefaultAsync(new PropertyBagSpecification(NotificationSources.NodeHealthyCheck));
            if (propertyBag == null)
            {
                result = new NodeHealthyCheckConfiguration();
                propertyBag = new PropertyBag();
                propertyBag.Add("Id", NotificationSources.NodeHealthyCheck);
                propertyBag.Add("Value", JsonSerializer.Serialize(result));
                propertyBag["CreatedDate"] = DateTime.UtcNow;
                await repo.AddAsync(propertyBag);
            }
            else
            {
                result = JsonSerializer.Deserialize<NodeHealthyCheckConfiguration>(propertyBag["Value"] as string);
            }

            rsp.SetResult(result);
        }
        catch (Exception ex)
        {
            rsp.ErrorCode = ex.HResult;
            rsp.Message = ex.ToString();
        }

        return rsp;
    }

    [HttpPost("/api/Configuration/NotificationSource/NodeHealthyCheck/Update")]
    public async Task<ApiResponse> UpdateNodeHealthyCheckConfigurationAsync(
        [FromBody] NodeHealthyCheckConfiguration model, CancellationToken cancellationToken = default)
    {
        var rsp = new ApiResponse();
        try
        {
            var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<PropertyBag>>();
            using var repo = repoFactory.CreateRepository();
            var propertyBag =
                await repo.FirstOrDefaultAsync(new PropertyBagSpecification(NotificationSources.NodeHealthyCheck));
            propertyBag["Value"] = JsonSerializer.Serialize(model);
            await repo.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            rsp.ErrorCode = ex.HResult;
            rsp.Message = ex.ToString();
        }

        return rsp;
    }

    [HttpGet("/api/Configuration/NotificationSource/DataQualityCheck")]
    public async Task<ApiResponse<DataQualityCheckConfiguration>> QueryDataQualityCheckConfigurationAsync()
    {
        var rsp = new ApiResponse<DataQualityCheckConfiguration>();
        try
        {
            DataQualityCheckConfiguration? result = null;
            var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<PropertyBag>>();
            using var repo = repoFactory.CreateRepository();
            var propertyBag =
                await repo.FirstOrDefaultAsync(new PropertyBagSpecification(NotificationSources.DataQualityCheck));
            if (propertyBag == null)
            {
                result = new DataQualityCheckConfiguration();
                propertyBag = new PropertyBag();
                propertyBag.Add("Id", NotificationSources.DataQualityCheck);
                propertyBag.Add("Value", JsonSerializer.Serialize(result));
                propertyBag["CreatedDate"] = DateTime.UtcNow;
                await repo.AddAsync(propertyBag);
            }
            else
            {
                result = JsonSerializer.Deserialize<DataQualityCheckConfiguration>(propertyBag["Value"] as string);
            }

            rsp.SetResult(result);
        }
        catch (Exception ex)
        {
            rsp.ErrorCode = ex.HResult;
            rsp.Message = ex.ToString();
        }

        return rsp;
    }

    [HttpPost("/api/Configuration/NotificationSource/DataQualityCheck/Update")]
    public async Task<ApiResponse> UpdateDataQualityCheckConfigurationAsync(
        [FromBody] DataQualityCheckConfiguration model, CancellationToken cancellationToken = default)
    {
        var rsp = new ApiResponse();
        try
        {
            var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<PropertyBag>>();
            using var repo = repoFactory.CreateRepository();
            var propertyBag =
                await repo.FirstOrDefaultAsync(new PropertyBagSpecification(NotificationSources.DataQualityCheck));
            propertyBag["Value"] = JsonSerializer.Serialize(model);
            await repo.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            rsp.ErrorCode = ex.HResult;
            rsp.Message = ex.ToString();
        }

        return rsp;
    }
}