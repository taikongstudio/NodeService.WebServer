namespace NodeService.WebServer.Controllers
{
    public partial class CommonConfigController
    {
        [HttpPost("/api/commonconfig/notification/addorupdate")]
        public Task<ApiResponse> AddOrUpdateAsync([FromBody] NotificationConfigModel model)
        {
            return AddOrUpdateConfigurationAsync(model);
        }

        [HttpGet("/api/commonconfig/notification/list")]
        public Task<PaginationResponse<NotificationConfigModel>> QueryNotificationConfigurationListAsync([FromQuery] QueryParametersModel queryParameters)
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
            return RemoveConfigurationAsync(model);
        }

        [HttpPost("/api/commonconfig/notification/{id}/invoke")]
        public async Task<ApiResponse> InvokeAsync(string id, [FromBody] InvokeNotificationParameters parameters)
        {
            var rsp = await QueryNotificationConfigAsync(id);
            if (rsp.ErrorCode != 0)
            {
                return rsp;
            }
            try
            {
                await _notificationMessageQueue.EnqueueAsync(new(parameters.Subject, parameters.Message, rsp.Result.Value));
            }
            catch (Exception ex)
            {
                rsp.ErrorCode = ex.HResult;
                rsp.Message = ex.ToString();
            }

            return rsp;
        }

    }
}
