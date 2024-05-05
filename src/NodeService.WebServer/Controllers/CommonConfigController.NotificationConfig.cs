namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/commonconfig/notification/addorupdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] NotificationConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model);
    }

    [HttpGet("/api/commonconfig/notification/list")]
    public Task<PaginationResponse<NotificationConfigModel>> QueryNotificationConfigurationListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<NotificationConfigModel>(queryParameters);
    }

    [HttpGet("/api/commonconfig/notification/{id}")]
    public Task<ApiResponse<NotificationConfigModel>> QueryNotificationConfigAsync(string id)
    {
        return QueryConfigurationAsync<NotificationConfigModel>(id);
    }

    [HttpPost("/api/commonconfig/notification/remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] NotificationConfigModel model)
    {
        return DeleteConfigurationAsync(model);
    }

    [HttpPost("/api/commonconfig/notification/{id}/invoke")]
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

    [HttpGet("/api/commonconfig/notificationsource/nodehealthycheck")]
    public async Task<ApiResponse<NodeHealthyCheckConfiguration>> QueryNodeHealthyCheckConfigurationAsync()
    {
        var rsp = new ApiResponse<NodeHealthyCheckConfiguration>();
        try
        {
            NodeHealthyCheckConfiguration? result = null;
            using var dbContext = _dbContextFactory.CreateDbContext();
            var notificationSourceDictionary =
                await dbContext.PropertyBagDbSet.FindAsync(nameof(NotificationSources.NodeHealthyCheck));
            if (notificationSourceDictionary == null)
            {
                notificationSourceDictionary = new Dictionary<string, object>();
                result = new NodeHealthyCheckConfiguration();
                notificationSourceDictionary.Add("Id", NotificationSources.NodeHealthyCheck);
                notificationSourceDictionary.Add("Value", JsonSerializer.Serialize(result));
                notificationSourceDictionary["CreatedDate"] = DateTime.UtcNow;
                dbContext.PropertyBagDbSet.Add(notificationSourceDictionary);
                await dbContext.SaveChangesAsync();
            }
            else
            {
                result = JsonSerializer.Deserialize<NodeHealthyCheckConfiguration>(
                    notificationSourceDictionary["Value"] as string);
            }

            rsp.Result = result;
        }
        catch (Exception ex)
        {
            rsp.ErrorCode = ex.HResult;
            rsp.Message = ex.ToString();
        }

        return rsp;
    }

    [HttpPost("/api/commonconfig/notificationsource/nodehealthycheck/update")]
    public async Task<ApiResponse> UpdateNodeHealthyCheckConfigurationAsync(
        [FromBody] NodeHealthyCheckConfiguration model)
    {
        var rsp = new ApiResponse();
        try
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var notificationSourceDictionary =
                await dbContext.PropertyBagDbSet.FindAsync(nameof(NotificationSources.NodeHealthyCheck));

            notificationSourceDictionary["Value"] = JsonSerializer.Serialize(model);
            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            rsp.ErrorCode = ex.HResult;
            rsp.Message = ex.ToString();
        }

        return rsp;
    }
}