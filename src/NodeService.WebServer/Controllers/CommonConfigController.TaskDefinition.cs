using NodeService.Infrastructure.Concurrent;

namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/CommonConfig/TaskDefinition/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] TaskDefinitionModel model)
    {
        return AddOrUpdateConfigurationAsync(model, AddOrUpdateTaskDefinitionAsync);
    }

    private async Task AddOrUpdateTaskDefinitionAsync(TaskDefinitionModel taskDefinition)
    {
        await _serviceProvider.GetService<IAsyncQueue<TaskScheduleMessage>>().EnqueueAsync(
            new TaskScheduleMessage(TaskTriggerSource.Schedule, taskDefinition.Id,
                taskDefinition.TriggerType == TaskTriggerType.Manual));
    }


    [HttpPost("/api/CommonConfig/TaskDefinition/{taskDefinitionId}/Invoke")]
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


    [HttpGet("/api/CommonConfig/TaskDefinition/List")]
    public Task<PaginationResponse<TaskDefinitionModel>> QueryTaskDefinitionListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationListAsync<TaskDefinitionModel>(queryParameters);
    }

    [HttpGet("/api/CommonConfig/TaskDefinition/{id}")]
    public Task<ApiResponse<TaskDefinitionModel>> QueryTaskDefinitionAsync(string id)
    {
        return QueryConfigurationAsync<TaskDefinitionModel>(id);
    }


    [HttpPost("/api/CommonConfig/TaskDefinition/Remove")]
    public Task<ApiResponse> RemoveAsync([FromBody] TaskDefinitionModel model)
    {
        return DeleteConfigurationAsync(model, RemoveTaskDefinitionAsync);
    }

    private async Task RemoveTaskDefinitionAsync(TaskDefinitionModel taskDefinition)
    {
        var messageQueue = _serviceProvider.GetService<IAsyncQueue<TaskScheduleMessage>>();
        await messageQueue.EnqueueAsync(new TaskScheduleMessage(TaskTriggerSource.Schedule, taskDefinition.Id,
            true));
    }
}