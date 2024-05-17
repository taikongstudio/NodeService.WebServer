using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;

namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/CommonConfig/notification/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] NotificationConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model);
    }

    [HttpGet("/api/CommonConfig/notification/List")]
    public Task<PaginationResponse<NotificationConfigModel>> QueryNotificationConfigurationListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<NotificationConfigModel>(queryParameters);
    }

    [HttpGet("/api/CommonConfig/notification/{id}")]
    public Task<ApiResponse<NotificationConfigModel>> QueryNotificationConfigAsync(string id)
    {
        return QueryConfigurationAsync<NotificationConfigModel>(id);
    }

    [HttpPost("/api/CommonConfig/notification/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] NotificationConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }

    [HttpPost("/api/CommonConfig/notification/{id}/Invoke")]
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

    [HttpGet("/api/CommonConfig/NotificationSource/nodehealthycheck")]
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

    [HttpPost("/api/CommonConfig/NotificationSource/nodehealthycheck/update")]
    public async Task<ApiResponse> UpdateNodeHealthyCheckConfigurationAsync(
        [FromBody] NodeHealthyCheckConfiguration model)
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
}