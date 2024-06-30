using NodeService.Infrastructure.Concurrent;
using NodeService.WebServer.Services.Tasks;

namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpPost("/api/CommonConfig/TaskDefinition/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync([FromBody] TaskDefinitionModel model)
    {
        return AddOrUpdateConfigurationAsync(model, AddOrUpdateTaskDefinitionAsync);
    }

    async ValueTask AddOrUpdateTaskDefinitionAsync(TaskDefinitionModel taskDefinition)
    {
        if (taskDefinition.TaskFlowTemplateId == null && taskDefinition.Value.TriggerType == TaskTriggerType.Schedule)
        {
            var taskScheduleParameters = new TaskScheduleParameters(TriggerSource.Schedule, taskDefinition.Id);
            var taskScheduleServiceParameters = new TaskScheduleServiceParameters(taskScheduleParameters);
            var op = new BatchQueueOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>(
                taskScheduleServiceParameters,
                BatchQueueOperationKind.AddOrUpdate);
            var queue = _serviceProvider.GetService<IAsyncQueue<BatchQueueOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>>>();
            await queue.EnqueueAsync(op);
        }
    }


    [HttpPost("/api/CommonConfig/TaskDefinition/{taskDefinitionId}/Invoke")]
    public async Task<ApiResponse<InvokeTaskResult>> InvokeTaskAsync(string taskDefinitionId,
        [FromBody] InvokeTaskParameters invokeTaskParameters)
    {
        var apiResponse = new ApiResponse<InvokeTaskResult>();
        try
        {
            var batchQueue = _serviceProvider.GetService<BatchQueue<TaskActivateServiceParameters>>();
            var fireInstanceId = $"Manual_{Guid.NewGuid()}";
            await batchQueue.SendAsync(new TaskActivateServiceParameters(new FireTaskParameters
            {
                FireTimeUtc = DateTime.UtcNow,
                TriggerSource = TriggerSource.Manual,
                FireInstanceId = fireInstanceId,
                TaskDefinitionId = taskDefinitionId,
                ScheduledFireTimeUtc = DateTime.UtcNow,
                NodeList = invokeTaskParameters.NodeList,
                EnvironmentVariables = invokeTaskParameters.EnvironmentVaribales
            }));
            apiResponse.SetResult(new InvokeTaskResult()
            {
                FireInstanceId = fireInstanceId,
                TaskDefinitionId = taskDefinitionId
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
        var messageQueue = _serviceProvider.GetService<IAsyncQueue<BatchQueueOperation<TaskScheduleServiceParameters,TaskScheduleServiceResult>>>();
        var taskScheduleParameters = new TaskScheduleParameters(TriggerSource.Schedule, taskDefinition.Id);
        var taskScheduleServiceParameters = new TaskScheduleServiceParameters(taskScheduleParameters);
        var op = new BatchQueueOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>(
            taskScheduleServiceParameters,
            BatchQueueOperationKind.Delete);
        await messageQueue.EnqueueAsync(op);
    }

    [HttpGet("/api/CommonConfig/TaskDefinition/VersionList")]
    public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryTaskDefinitionVersionListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters);
    }

    [HttpPost("/api/CommonConfig/TaskDefinition/SwitchVersion")]
    public Task<ApiResponse> SwitchTaskDefinitionVersionAsync(
        [FromBody] ConfigurationVersionSwitchParameters parameters)
    {
        return SwitchConfigurationVersionAsync<TaskDefinitionModel>(parameters);
    }

    [HttpPost("/api/CommonConfig/TaskDefinition/DeleteVersion")]
    public Task<ApiResponse> DeleteTaskDefinitionVersionAsync(
        [FromBody] ConfigurationVersionRecordModel entity)
    {
        return DeleteConfigurationVersionAsync<TaskDefinitionModel>(new ConfigurationVersionDeleteParameters(entity));
    }

}