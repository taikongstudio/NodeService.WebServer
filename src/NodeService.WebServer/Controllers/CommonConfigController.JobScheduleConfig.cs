using NodeService.Infrastructure.Concurrent;

namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/commonconfig/jobschedule/addorupdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] JobScheduleConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model, AddOrUpdateJobScheduleConfigAsync);
    }

    async Task AddOrUpdateJobScheduleConfigAsync(JobScheduleConfigModel jobScheduleConfig)
    {
        await _serviceProvider.GetService<IAsyncQueue<TaskScheduleMessage>>().EnqueueAsync(
            new TaskScheduleMessage(TaskTriggerSource.Schedule, jobScheduleConfig.Id,
                jobScheduleConfig.TriggerType == JobScheduleTriggerType.Manual));
    }


    [HttpPost("/api/commonconfig/jobschedule/{jobScheduleId}/invoke")]
    public async Task<ApiResponse> InvokeJobScheduleAsync(string jobScheduleId,
        [FromBody] InvokeTaskScheduleParameters invokeJobScheduleParameters)
    {
        var apiResponse = new ApiResponse();
        try
        {
            await _serviceProvider.GetService<IAsyncQueue<TaskScheduleMessage>>().EnqueueAsync(
                new TaskScheduleMessage(TaskTriggerSource.Manual, jobScheduleId));
        }
        catch (Exception ex)
        {
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }

        return apiResponse;
    }


    [HttpGet("/api/commonconfig/jobschedule/list")]
    public Task<PaginationResponse<JobScheduleConfigModel>> QueryJobScheduleConfigurationListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<JobScheduleConfigModel>(queryParameters);
    }

    [HttpGet("/api/commonconfig/jobschedule/{id}")]
    public Task<ApiResponse<JobScheduleConfigModel>> QueryJobScheduleConfigAsync(string id)
    {
        return QueryConfigurationAsync<JobScheduleConfigModel>(id);
    }


    [HttpPost("/api/commonconfig/jobschedule/remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] JobScheduleConfigModel model)
    {
        return DeleteConfigurationAsync(model, RemoveJobScheduleConfigAsync);
    }

    async Task RemoveJobScheduleConfigAsync(JobScheduleConfigModel jobScheduleConfig)
    {
        var messageQueue = _serviceProvider.GetService<IAsyncQueue<TaskScheduleMessage>>();
        await messageQueue.EnqueueAsync(new TaskScheduleMessage(TaskTriggerSource.Schedule, jobScheduleConfig.Id,
            true));
    }
}