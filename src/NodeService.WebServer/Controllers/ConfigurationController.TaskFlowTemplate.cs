using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.DataModels;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.DataQueue;
using NodeService.WebServer.Services.Tasks;
using System.Threading.Tasks;
using JobScheduler = NodeService.WebServer.Services.Tasks.JobScheduler;

namespace NodeService.WebServer.Controllers
{
    public partial class ConfigurationController
    {
        [HttpPost("/api/CommonConfig/TaskFlowTemplate/AddOrUpdate")]
        public Task<ApiResponse> AddOrUpdateAsync([FromBody] TaskFlowTemplateModel model, CancellationToken cancellationToken = default)
        {
            return AddOrUpdateConfigurationAsync(model, OnTaskFlowTemplateVersionChanged, cancellationToken: cancellationToken);
        }

        async ValueTask OnTaskFlowTemplateVersionChanged(
        ConfigurationSaveChangesResult result,
        CancellationToken cancellationToken = default)
        {

            switch (result.Type)
            {
                case ConfigurationChangedType.None:
                    break;
                case ConfigurationChangedType.Add:
                case ConfigurationChangedType.Update:
                    if (result.NewValue is TaskFlowTemplateModel  taskFlowTemplate)
                    {
                        await AddOrUpdateTaskFlowTemplateAsync(taskFlowTemplate, cancellationToken);
                    }
                    break;
                case ConfigurationChangedType.Delete:
                    if (result.OldValue is TaskFlowTemplateModel  oldTaskFlowTemplate)
                    {
                        await RemoveTaskFlowTemplateAsync(oldTaskFlowTemplate, cancellationToken);
                    }
                    break;
                default:
                    break;
            }

        }

        async ValueTask AddOrUpdateTaskFlowTemplateAsync(
            TaskFlowTemplateModel taskFlowTemplate,
            CancellationToken cancellationToken = default)
        {
            var triggerTask = taskFlowTemplate.GetTriggerTask();
            var taskScheduleParameters = new TaskFlowScheduleParameters(TriggerSource.Schedule, taskFlowTemplate.Id);
            var taskScheduleServiceParameters = new TaskScheduleServiceParameters(taskScheduleParameters);
            var op = new BatchQueueOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>(
            taskScheduleServiceParameters,
              triggerTask.TriggerType == TaskTriggerType.Manual ? BatchQueueOperationKind.Delete : BatchQueueOperationKind.AddOrUpdate);
            var queue = _serviceProvider.GetService<IAsyncQueue<BatchQueueOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>>>();
            await queue.EnqueueAsync(op, cancellationToken);

        }

        async ValueTask RemoveTaskFlowTemplateAsync(
            TaskFlowTemplateModel taskFlowTemplate,
            CancellationToken cancellationToken = default)
        {
            var messageQueue = _serviceProvider.GetService<IAsyncQueue<BatchQueueOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>>>();
            var taskScheduleParameters = new TaskFlowScheduleParameters(TriggerSource.Schedule, taskFlowTemplate.Id);
            var taskScheduleServiceParameters = new TaskScheduleServiceParameters(taskScheduleParameters);
            var op = new BatchQueueOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>(
                taskScheduleServiceParameters,
                BatchQueueOperationKind.Delete);
            await messageQueue.EnqueueAsync(op, cancellationToken);
        }

        [HttpGet("/api/CommonConfig/TaskFlowTemplate/List")]
        public Task<PaginationResponse<TaskFlowTemplateModel>> QueryTaskFlowTemplateListAsync(
            [FromQuery] PaginationQueryParameters queryParameters,
            CancellationToken cancellationToken = default)
        {
            return QueryConfigurationListAsync<TaskFlowTemplateModel>(queryParameters, cancellationToken: cancellationToken);
        }

        [HttpGet("/api/CommonConfig/TaskFlowTemplate/{id}")]
        public Task<ApiResponse<TaskFlowTemplateModel>> QueryTaskFlowTemplateAsync(
            string id,
            CancellationToken cancellationToken = default)
        {
            return QueryConfigurationAsync<TaskFlowTemplateModel>(id, cancellationToken: cancellationToken);
        }


        [HttpPost("/api/CommonConfig/TaskFlowTemplate/Remove")]
        public Task<ApiResponse> RemoveAsync(
            [FromBody] TaskFlowTemplateModel model,
            CancellationToken cancellationToken = default)
        {
            return DeleteConfigurationAsync(model, OnTaskDefinitionVersionChanged, cancellationToken: cancellationToken);
        }

        [HttpGet("/api/CommonConfig/TaskFlowTemplate/VersionList")]
        public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryTaskFlowTemplateConfigurationVersionListAsync(
            [FromQuery] PaginationQueryParameters queryParameters,
            CancellationToken cancellationToken = default)
        {
            return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters, cancellationToken: cancellationToken);
        }

        [HttpPost("/api/CommonConfig/TaskFlowTemplate/SwitchVersion")]
        public Task<ApiResponse> SwitchTaskFlowTemplateConfigurationVersionAsync(
            [FromBody] ConfigurationVersionSwitchParameters parameters,
            CancellationToken cancellationToken = default)
        {
            return SwitchConfigurationVersionAsync<TaskFlowTemplateModel>(parameters, cancellationToken: cancellationToken);
        }

        [HttpPost("/api/CommonConfig/TaskFlowTemplate/DeleteVersion")]
        public Task<ApiResponse> DeleteTaskFlowTemplateConfigurationVersionAsync(
            [FromBody] ConfigurationVersionRecordModel entity,
            CancellationToken cancellationToken = default)
        {
            return DeleteConfigurationVersionAsync<TaskFlowTemplateModel>(new ConfigurationVersionDeleteParameters(entity), cancellationToken);
        }

        [HttpPost("/api/CommonConfig/TaskFlowTemplate/{taskFlowTemplateId}/Invoke")]
        public async Task<ApiResponse<InvokeTaskFlowResult>> InvokeTaskFlowAsync(
            string taskFlowTemplateId,
            [FromBody] InvokeTaskFlowParameters invokeTaskFlowParameters,
            CancellationToken cancellationToken = default)
        {
            var apiResponse = new ApiResponse<InvokeTaskFlowResult>();
            try
            {
                var batchQueue = _serviceProvider.GetService<BatchQueue<TaskActivateServiceParameters>>();
                var taskFlowInstanceId = $"Manual_TaskFlow_{Guid.NewGuid()}";
                var fireInstanceId = $"Manual_{Guid.NewGuid()}";
                await batchQueue.SendAsync(new TaskActivateServiceParameters(new FireTaskFlowParameters
                {
                    TaskFlowTemplateId = taskFlowTemplateId,
                    FireTimeUtc = DateTime.UtcNow,
                    TriggerSource = TriggerSource.Manual,
                    TaskFlowParentInstanceId = null,
                    TaskFlowInstanceId = taskFlowInstanceId,
                    ScheduledFireTimeUtc = DateTime.UtcNow,
                }), cancellationToken);
                apiResponse.SetResult(new InvokeTaskFlowResult()
                {
                    TaskFlowInstanceId = taskFlowInstanceId
                });
            }
            catch (Exception ex)
            {
                apiResponse.ErrorCode = ex.HResult;
                apiResponse.Message = ex.Message;
            }

            return apiResponse;
        }

    }
}
