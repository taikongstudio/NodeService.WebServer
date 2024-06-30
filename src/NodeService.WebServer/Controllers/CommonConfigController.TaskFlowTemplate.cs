using NodeService.Infrastructure.Concurrent;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Tasks;

namespace NodeService.WebServer.Controllers
{
    public partial class CommonConfigController
    {
        [HttpPost("/api/CommonConfig/TaskFlowTemplate/AddOrUpdate")]
        public Task<ApiResponse> AddOrUpdateAsync([FromBody] TaskFlowTemplateModel model)
        {
            return AddOrUpdateConfigurationAsync(model, AddOrUpdateTaskFlowTemplateAsync);
        }

        private async ValueTask AddOrUpdateTaskFlowTemplateAsync(TaskFlowTemplateModel taskFlowTemplate)
        {
            var firstStage = taskFlowTemplate.Value.TaskStages.FirstOrDefault();
            if (firstStage == null)
            {
                return;
            }
            var triggerGroup = firstStage.TaskGroups.FirstOrDefault();
            if (triggerGroup == null)
            {
                return;
            }
            var triggerTask = triggerGroup.Tasks.FirstOrDefault();
            if (triggerTask == null)
            {
                return;
            }
            var taskDefinitionRepoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<TaskDefinitionModel>>();
            using var taskDefinitionRepo = taskDefinitionRepoFactory.CreateRepository();
            var taskDefinition = await taskDefinitionRepo.GetByIdAsync(triggerTask.TaskDefinitionId);
            if (taskDefinition == null)
            {
                return;
            }

            foreach (var taskStage in taskFlowTemplate.Value.TaskStages)
            {
                foreach (var taskGroup in taskStage.TaskGroups)
                {
                    foreach (var task in taskGroup.Tasks)
                    {

                    }
                }
            }

            await AddOrUpdateTaskDefinitionAsync(taskDefinition);


        }

        [HttpGet("/api/CommonConfig/TaskFlowTemplate/List")]
        public Task<PaginationResponse<TaskFlowTemplateModel>> QueryTaskFlowTemplateListAsync(
            [FromQuery] PaginationQueryParameters queryParameters)
        {
            return QueryConfigurationListAsync<TaskFlowTemplateModel>(queryParameters);
        }

        [HttpGet("/api/CommonConfig/TaskFlowTemplate/{id}")]
        public Task<ApiResponse<TaskFlowTemplateModel>> QueryTaskFlowTemplateAsync(string id)
        {
            return QueryConfigurationAsync<TaskFlowTemplateModel>(id);
        }


        [HttpPost("/api/CommonConfig/TaskFlowTemplate/Remove")]
        public Task<ApiResponse> RemoveAsync([FromBody] TaskFlowTemplateModel model)
        {
            return DeleteConfigurationAsync(model);
        }

        [HttpGet("/api/CommonConfig/TaskFlowTemplate/VersionList")]
        public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryTaskFlowTemplateConfigurationVersionListAsync(
        [FromQuery] PaginationQueryParameters queryParameters)
        {
            return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters);
        }

        [HttpPost("/api/CommonConfig/TaskFlowTemplate/SwitchVersion")]
        public Task<ApiResponse> SwitchTaskFlowTemplateConfigurationVersionAsync(
            [FromBody] ConfigurationVersionSwitchParameters parameters)
        {
            return SwitchConfigurationVersionAsync<TaskFlowTemplateModel>(parameters);
        }

        [HttpPost("/api/CommonConfig/TaskFlowTemplate/DeleteVersion")]
        public Task<ApiResponse> DeleteTaskFlowTemplateConfigurationVersionAsync(
            [FromBody] ConfigurationVersionRecordModel entity)
        {
            return DeleteConfigurationVersionAsync<TaskFlowTemplateModel>(new ConfigurationVersionDeleteParameters(entity));
        }

        [HttpPost("/api/CommonConfig/TaskFlowTemplate/{taskFlowTemplateId}/Invoke")]
        public async Task<ApiResponse<InvokeTaskFlowResult>> InvokeTaskFlowAsync(string taskFlowTemplateId,
[FromBody] InvokeTaskFlowParameters invokeTaskFlowParameters)
        {
            var apiResponse = new ApiResponse<InvokeTaskFlowResult>();
            try
            {
                var batchQueue = _serviceProvider.GetService<BatchQueue<TaskActivateServiceParameters>>();
                var taskFlowInstanceId = $"Manual_TaskFlow_{Guid.NewGuid()}";
                var fireInstanceId = $"Manual_{Guid.NewGuid()}";
                await batchQueue.SendAsync(new TaskActivateServiceParameters(new FireTaskFlowParameters
                {
                    TaskFlowTemplateId = invokeTaskFlowParameters.TaskFlowTemplateId,
                    FireTimeUtc = DateTime.UtcNow,
                    TriggerSource = TriggerSource.Manual,
                    ParentTaskFlowInstanceId = null,
                    TaskFlowInstanceId = taskFlowInstanceId,
                    ScheduledFireTimeUtc = DateTime.UtcNow,
                }));
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
