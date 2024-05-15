using NodeService.Infrastructure.Concurrent;

namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/CommonConfig/jobschedule/addorupdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] JobScheduleConfigModel model)
    {
        return AddOrUpdateConfigurationAsync(model, AddOrUpdateTaskDefinitionAsync);
    }

    private async Task AddOrUpdateTaskDefinitionAsync(JobScheduleConfigModel taskDefinition)
    {
        await _serviceProvider.GetService<IAsyncQueue<TaskScheduleMessage>>().EnqueueAsync(
            new TaskScheduleMessage(TaskTriggerSource.Schedule, taskDefinition.Id,
                taskDefinition.TriggerType == TaskTriggerType.Manual));
    }


    [HttpPost("/api/CommonConfig/jobschedule/{taskDefinitionId}/invoke")]
    public async Task<ApiResponse> InvokeJobScheduleAsync(string taskDefinitionId,
        [FromBody] InvokeTaskParameters invokeTaskParameters)
    {
        var apiResponse = new ApiResponse();
        try
        {
            var batchQueue = _serviceProvider.GetService<BatchQueue<FireTaskParameters>>();
            await batchQueue.SendAsync(new FireTaskParameters
            {
                FireTimeUtc = DateTime.UtcNow,
                TriggerSource = TaskTriggerSource.Manual,
                FireInstanceId = $"Manual_{Guid.NewGuid()}",
                TaskDefinitionId = taskDefinitionId,
                ScheduledFireTimeUtc = DateTime.UtcNow,
                NodeList = invokeTaskParameters.NodeList,
                EnvironmentVariables = invokeTaskParameters.EnvironmentVaribales
            });
        }
        catch (Exception ex)
        {
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }

        return apiResponse;
    }


    [HttpGet("/api/CommonConfig/jobschedule/list")]
    public Task<PaginationResponse<JobScheduleConfigModel>> QueryTaskDefinitionListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<JobScheduleConfigModel>(queryParameters);
    }

    [HttpGet("/api/CommonConfig/jobschedule/{id}")]
    public Task<ApiResponse<JobScheduleConfigModel>> QueryTaskDefinitionAsync(string id)
    {
        return QueryConfigurationAsync<JobScheduleConfigModel>(id);
    }


    [HttpPost("/api/CommonConfig/jobschedule/remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] JobScheduleConfigModel model)
    {
        return DeleteConfigurationAsync(model, RemoveTaskDefinitionAsync);
    }

    private async Task RemoveTaskDefinitionAsync(JobScheduleConfigModel taskDefinition)
    {
        var messageQueue = _serviceProvider.GetService<IAsyncQueue<TaskScheduleMessage>>();
        await messageQueue.EnqueueAsync(new TaskScheduleMessage(TaskTriggerSource.Schedule, taskDefinition.Id,
            true));
    }
}