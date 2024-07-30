using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.DataModels;
using NodeService.WebServer.Services.DataQueue;
using NodeService.WebServer.Services.Tasks;
using System.Security.Cryptography;

namespace NodeService.WebServer.Controllers;

public partial class ConfigurationController
{
    [HttpPost("/api/CommonConfig/TaskDefinition/AddOrUpdate")]
    public Task<ApiResponse> AddOrUpdateAsync(
        [FromBody] TaskDefinitionModel model,
        CancellationToken cancellationToken = default)
    {
        return AddOrUpdateConfigurationAsync(model, OnTaskDefinitionVersionChanged, cancellationToken);
    }


    [HttpPost("/api/CommonConfig/TaskDefinition/{taskDefinitionId}/Invoke")]
    public async Task<ApiResponse<InvokeTaskResult>> InvokeTaskAsync(
        string taskDefinitionId,
        [FromBody] InvokeTaskParameters invokeTaskParameters,
        CancellationToken cancellationToken = default)
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
                EnvironmentVariables = invokeTaskParameters.EnvironmentVariables
            }), cancellationToken);
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
        [FromQuery] PaginationQueryParameters queryParameters,
        CancellationToken cancellationToken = default)
    {
        return QueryConfigurationListAsync<TaskDefinitionModel>(queryParameters, cancellationToken);
    }

    [HttpGet("/api/CommonConfig/TaskDefinition/{id}")]
    public Task<ApiResponse<TaskDefinitionModel>> QueryTaskDefinitionAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        return QueryConfigurationAsync<TaskDefinitionModel>(id, cancellationToken: cancellationToken);
    }


    [HttpPost("/api/CommonConfig/TaskDefinition/Remove")]
    public Task<ApiResponse> RemoveAsync(
        [FromBody] TaskDefinitionModel model,
        CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationAsync(model, OnTaskDefinitionVersionChanged, cancellationToken);
    }

    private async ValueTask RemoveTaskDefinitionAsync(
        TaskDefinitionModel taskDefinition,
        CancellationToken cancellationToken = default)
    {
        var messageQueue = _serviceProvider.GetService<IAsyncQueue<AsyncOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>>>();
        var taskScheduleParameters = new TaskScheduleParameters(TriggerSource.Schedule, taskDefinition.Id);
        var taskScheduleServiceParameters = new TaskScheduleServiceParameters(taskScheduleParameters);
        var op = new AsyncOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>(
            taskScheduleServiceParameters,
            AsyncOperationKind.Delete);
        await messageQueue.EnqueueAsync(op, cancellationToken);
    }

    [HttpGet("/api/CommonConfig/TaskDefinition/VersionList")]
    public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryTaskDefinitionVersionListAsync(
        [FromQuery] PaginationQueryParameters queryParameters,
        CancellationToken cancellationToken = default)
    {
        return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(
            queryParameters,
            cancellationToken);
    }

    async ValueTask OnTaskDefinitionVersionChanged(
        ConfigurationSaveChangesResult result,
        CancellationToken cancellationToken = default)
    {

        switch (result.Type)
        {
            case ConfigurationChangedType.None:
                break;
            case ConfigurationChangedType.Add:
            case ConfigurationChangedType.Update:
                if (result.NewValue is TaskDefinitionModel newTaskDefinition)
                {
                    await AddOrUpdateTaskDefinitionAsync(newTaskDefinition, cancellationToken);
                }
                break;
            case ConfigurationChangedType.Delete:
                if (result.OldValue is TaskDefinitionModel oldTaskDefinition)
                {
                    await RemoveTaskDefinitionAsync(oldTaskDefinition, cancellationToken);
                }
                break;
            default:
                break;
        }

    }

    private async Task AddOrUpdateTaskDefinitionAsync(
        TaskDefinitionModel taskDefinition,
        CancellationToken cancellationToken = default)
    {
        if (taskDefinition.TaskFlowTemplateId == null)
        {
            var taskScheduleParameters = new TaskScheduleParameters(TriggerSource.Schedule, taskDefinition.Id);
            var taskScheduleServiceParameters = new TaskScheduleServiceParameters(taskScheduleParameters);
            var op = new AsyncOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>(
            taskScheduleServiceParameters,
              taskDefinition.Value.TriggerType == TaskTriggerType.Manual || !taskDefinition.Value.IsEnabled ? AsyncOperationKind.Delete : AsyncOperationKind.AddOrUpdate);
            var queue = _serviceProvider.GetService<IAsyncQueue<AsyncOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>>>();
            await queue.EnqueueAsync(op, cancellationToken);
        }
    }

    [HttpPost("/api/CommonConfig/TaskDefinition/SwitchVersion")]
    public Task<ApiResponse> SwitchTaskDefinitionVersionAsync(
        [FromBody] ConfigurationVersionSwitchParameters parameters,
        CancellationToken cancellationToken = default)
    {
        return SwitchConfigurationVersionAsync<TaskDefinitionModel>(
            parameters,
            OnTaskDefinitionVersionChanged,
            cancellationToken: cancellationToken);
    }

    [HttpPost("/api/CommonConfig/TaskDefinition/DeleteVersion")]
    public Task<ApiResponse> DeleteTaskDefinitionVersionAsync(
        [FromBody] ConfigurationVersionRecordModel entity,
        CancellationToken cancellationToken = default)
    {
        return DeleteConfigurationVersionAsync<TaskDefinitionModel>(
            new ConfigurationVersionDeleteParameters(entity),
            cancellationToken);
    }

}