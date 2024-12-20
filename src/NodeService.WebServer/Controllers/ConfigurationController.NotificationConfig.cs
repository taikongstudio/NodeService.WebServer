﻿using NodeService.Infrastructure.Concurrent;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.DataServices;
using NodeService.WebServer.Services.TaskSchedule;
using NodeService.WebServer.UI.Pages.Notifications.Components;

namespace NodeService.WebServer.Controllers;

public partial class ConfigurationController
{
    [HttpPost("/api/CommonConfig/Notification/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] NotificationConfigModel model, CancellationToken cancellationToken = default)
    {
        return AddOrUpdateConfigurationAsync(model);
    }

    [HttpGet("/api/CommonConfig/Notification/List")]
    public Task<PaginationResponse<NotificationConfigModel>> QueryNotificationConfigurationListAsync(
        [FromQuery] PaginationQueryParameters queryParameters,
        CancellationToken cancellationToken = default)
    {
        return QueryConfigurationListAsync<NotificationConfigModel>(
            queryParameters,
            cancellationToken);
    }

    [HttpGet("/api/CommonConfig/Notification/VersionList")]
    public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryNotificationConfigurationVersionListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters);
    }

    [HttpPost("/api/CommonConfig/Notification/SwitchVersion")]
    public Task<ApiResponse> SwitchNotificationVersionAsync(
        [FromBody] ConfigurationVersionSwitchParameters parameters, CancellationToken cancellationToken = default)
    {
        return SwitchConfigurationVersionAsync<NotificationConfigModel>(parameters);
    }

    [HttpPost("/api/CommonConfig/Notification/DeleteVersion")]
    public Task<ApiResponse> DeleteNotificationConfigurationVersionAsync(
    [FromBody] ConfigurationVersionRecordModel entity, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationVersionAsync<NotificationConfigModel>(new ConfigurationVersionDeleteParameters(entity));
    }

    [HttpGet("/api/CommonConfig/Notification/{id}")]
    public Task<ApiResponse<NotificationConfigModel>> QueryNotificationConfigAsync(string id)
    {
        return QueryConfigurationAsync<NotificationConfigModel>(id);
    }

    [HttpPost("/api/CommonConfig/Notification/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] NotificationConfigModel model, CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationAsync(model);
    }

    [HttpPost("/api/CommonConfig/Notification/{id}/Invoke")]
    public async Task<ApiResponse> InvokeAsync(string id, [FromBody] InvokeNotificationParameters parameters)
    {
        var rsp = await QueryNotificationConfigAsync(id);
        if (rsp.ErrorCode != 0) return rsp;
        try
        {
            var _notificationMessageQueue = _serviceProvider.GetService<IAsyncQueue<NotificationMessage>>();
            await _notificationMessageQueue.EnqueueAsync(new NotificationMessage(new EmailContent(parameters.Subject, parameters.Message, []),
                rsp.Result.Value));
        }
        catch (Exception ex)
        {
            rsp.ErrorCode = ex.HResult;
            rsp.Message = ex.ToString();
        }

        return rsp;
    }

    [HttpGet("/api/CommonConfig/NotificationSource/NodeHealthyCheck")]
    public async Task<ApiResponse<NodeHealthyCheckConfiguration>> QueryNodeHealthyCheckConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var rsp = new ApiResponse<NodeHealthyCheckConfiguration>();
        try
        {

            var result = await _configurationQueryService.QueryNodeHealthyCheckConfigurationAsync(cancellationToken);
            rsp.SetResult(result);
        }
        catch (Exception ex)
        {
            rsp.ErrorCode = ex.HResult;
            rsp.Message = ex.ToString();
        }

        return rsp;
    }

    [HttpPost("/api/CommonConfig/NotificationSource/NodeHealthyCheck/Update")]
    public async Task<ApiResponse> UpdateNodeHealthyCheckConfigurationAsync(
        [FromBody] NodeHealthyCheckConfiguration entity,
        CancellationToken cancellationToken = default)
    {
        var rsp = new ApiResponse();
        try
        {

            await _configurationQueryService.UpdateNodeHealthyCheckConfigurationAsync(entity, cancellationToken);
        }
        catch (Exception ex)
        {
            rsp.ErrorCode = ex.HResult;
            rsp.Message = ex.ToString();
        }

        return rsp;
    }

    [HttpGet("/api/CommonConfig/NotificationSource/DataQualityCheck")]
    public async Task<ApiResponse<DataQualityCheckConfiguration>> QueryDataQualityCheckConfigurationAsync()
    {
        var rsp = new ApiResponse<DataQualityCheckConfiguration>();
        try
        {
            DataQualityCheckConfiguration? result = null;
            var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<PropertyBag>>();
            await using var repo = await repoFactory.CreateRepositoryAsync();
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

    [HttpPost("/api/CommonConfig/NotificationSource/DataQualityCheck/Update")]
    public async Task<ApiResponse> UpdateDataQualityCheckConfigurationAsync(
        [FromBody] DataQualityCheckConfiguration entity,
        CancellationToken cancellationToken = default)
    {
        var rsp = new ApiResponse();
        try
        {
            var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<PropertyBag>>();
            await using var repo = await repoFactory.CreateRepositoryAsync();
            var propertyBag = await repo.FirstOrDefaultAsync(new PropertyBagSpecification(NotificationSources.DataQualityCheck), cancellationToken);
            propertyBag["Value"] = JsonSerializer.Serialize(entity);
            await repo.SaveChangesAsync(cancellationToken);

        }
        catch (Exception ex)
        {
            rsp.ErrorCode = ex.HResult;
            rsp.Message = ex.ToString();
        }

        return rsp;
    }

    [HttpGet("/api/CommonConfig/NotificationSource/TaskObservation")]
    public async Task<ApiResponse<TaskObservationConfiguration>> QueryTaskObservationConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var rsp = new ApiResponse<TaskObservationConfiguration>();
        try
        {

            var result = await _configurationQueryService.QueryTaskObservationConfigurationAsync(cancellationToken);
            rsp.SetResult(result);
        }
        catch (Exception ex)
        {
            rsp.ErrorCode = ex.HResult;
            rsp.Message = ex.ToString();
        }

        return rsp;
    }

    [HttpPost("/api/CommonConfig/NotificationSource/TaskObservation/Update")]
    public async Task<ApiResponse> UpdateTaskObservationConfigurationAsync(
        [FromBody] TaskObservationConfiguration entity,
        CancellationToken cancellationToken = default)
    {
        var rsp = new ApiResponse();
        try
        {
            await _configurationQueryService.UpdateTaskObservationConfigurationAsync(entity, cancellationToken);
        }
        catch (Exception ex)
        {
            rsp.ErrorCode = ex.HResult;
            rsp.Message = ex.ToString();
        }

        return rsp;
    }
}