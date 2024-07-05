using NodeService.Infrastructure.Concurrent;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.QueryOptimize;
using NodeService.WebServer.Services.Tasks;

namespace NodeService.WebServer.Controllers
{
    public partial class CommonConfigController
    {
        [HttpPost("/api/CommonConfig/TaskFlowTemplate/AddOrUpdate")]
        public Task<ApiResponse> AddOrUpdateAsync([FromBody] TaskFlowTemplateModel model, CancellationToken cancellationToken = default)
        {
            return AddOrUpdateConfigurationAsync(model, cancellationToken: cancellationToken);
        }

        private async ValueTask AddOrUpdateTaskFlowTemplateAsync(TaskFlowTemplateModel taskFlowTemplate, CancellationToken cancellationToken = default)
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
            var taskDefinition = await taskDefinitionRepo.GetByIdAsync(triggerTask.TaskDefinitionId, cancellationToken);
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
                        if (task.TemplateType == TaskFlowTaskTemplateType.TriggerTask && task.TriggerType == TaskTriggerType.Schedule)
                        {
                           // await AddOrUpdateTaskDefinitionAsync(taskDefinition, cancellationToken);
                        }
                    }
                }
            }




        }

        [HttpGet("/api/CommonConfig/TaskFlowTemplate/List")]
        public Task<PaginationResponse<TaskFlowTemplateModel>> QueryTaskFlowTemplateListAsync(
            [FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
        {
            return QueryConfigurationListAsync<TaskFlowTemplateModel>(queryParameters, cancellationToken: cancellationToken);
        }

        [HttpGet("/api/CommonConfig/TaskFlowTemplate/{id}")]
        public Task<ApiResponse<TaskFlowTemplateModel>> QueryTaskFlowTemplateAsync(string id,CancellationToken cancellationToken=default)
        {
            return QueryConfigurationAsync<TaskFlowTemplateModel>(id, cancellationToken: cancellationToken);
        }


        [HttpPost("/api/CommonConfig/TaskFlowTemplate/Remove")]
        public Task<ApiResponse> RemoveAsync([FromBody] TaskFlowTemplateModel model, CancellationToken cancellationToken = default)
        {
            return DeleteConfigurationAsync(model, cancellationToken: cancellationToken);
        }

        [HttpGet("/api/CommonConfig/TaskFlowTemplate/VersionList")]
        public Task<PaginationResponse<ConfigurationVersionRecordModel>> QueryTaskFlowTemplateConfigurationVersionListAsync(
        [FromQuery] PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
        {
            return QueryConfigurationVersionListAsync<ConfigurationVersionRecordModel>(queryParameters, cancellationToken: cancellationToken);
        }

        [HttpPost("/api/CommonConfig/TaskFlowTemplate/SwitchVersion")]
        public Task<ApiResponse> SwitchTaskFlowTemplateConfigurationVersionAsync(
            [FromBody] ConfigurationVersionSwitchParameters parameters, CancellationToken cancellationToken = default)
        {
            return SwitchConfigurationVersionAsync<TaskFlowTemplateModel>(parameters, cancellationToken: cancellationToken);
        }

        [HttpPost("/api/CommonConfig/TaskFlowTemplate/DeleteVersion")]
        public Task<ApiResponse> DeleteTaskFlowTemplateConfigurationVersionAsync(
            [FromBody] ConfigurationVersionRecordModel entity,CancellationToken cancellationToken=default)
        {
            return DeleteConfigurationVersionAsync<TaskFlowTemplateModel>(new ConfigurationVersionDeleteParameters(entity), cancellationToken);
        }

        [HttpPost("/api/CommonConfig/TaskFlowTemplate/{taskFlowTemplateId}/Invoke")]
        public async Task<ApiResponse<InvokeTaskFlowResult>> InvokeTaskFlowAsync(
            string taskFlowTemplateId,
            [FromBody] InvokeTaskFlowParameters invokeTaskFlowParameters,
            CancellationToken cancellationToken=default)
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
