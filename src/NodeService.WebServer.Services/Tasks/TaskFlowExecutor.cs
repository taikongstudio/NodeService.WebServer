using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Models;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.Tasks
{
    public class TaskFlowExecutor
    {
        readonly ILogger<TaskFlowExecutor> _logger;
        readonly ExceptionCounter _exceptionCounter;
        readonly BatchQueue<TaskActivateServiceParameters> _taskActivateServiceParametersBatchQueue;
        readonly ApplicationRepositoryFactory<TaskFlowTemplateModel> _taskFlowTemplateRepoFactory;

        public TaskFlowExecutor(
            ILogger<TaskFlowExecutor> logger,
            ExceptionCounter exceptionCounter,
            BatchQueue<TaskActivateServiceParameters> taskActivateServiceBatchQueue,
            ApplicationRepositoryFactory<TaskFlowTemplateModel> taskFlowTemplateRepoFactory
            )
        {
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _taskActivateServiceParametersBatchQueue = taskActivateServiceBatchQueue;
            _taskFlowTemplateRepoFactory = taskFlowTemplateRepoFactory;
        }

        public async ValueTask ExecuteAsync(
            TaskFlowExecutionInstanceModel taskFlowExecutionInstance,
            CancellationToken cancellationToken = default)
        {
            if (taskFlowExecutionInstance.Value.IsTerminatedStatus())
            {
                return;
            }
            using var taskFlowTemplateRepo = _taskFlowTemplateRepoFactory.CreateRepository();
            var taskFlowTemplate = await taskFlowTemplateRepo.GetByIdAsync(taskFlowExecutionInstance.Value.TaskFlowTemplateId, cancellationToken);
            if (taskFlowTemplate == null)
            {
                return;
            }
            foreach (var taskStageExecutionInstance in taskFlowExecutionInstance.TaskStages)
            {
                if (taskStageExecutionInstance.IsTerminatedStatus())
                {
                    continue;
                }
                await ExecuteTaskStageAsync(
                            taskFlowTemplate,
                            taskFlowExecutionInstance,
                            taskStageExecutionInstance,
                            cancellationToken);
                if (taskStageExecutionInstance.Status == TaskExecutionStatus.Finished)
                {
                    continue;
                }
                break;
            }
            if (taskFlowExecutionInstance.TaskStages.All(x => x.Status == TaskExecutionStatus.Finished))
            {
                taskFlowExecutionInstance.Value.Status = TaskExecutionStatus.Finished;
            }
            else if (taskFlowExecutionInstance.Value.TaskStages.Any(x => x.Status != TaskExecutionStatus.Finished))
            {
                taskFlowExecutionInstance.Value.Status = TaskExecutionStatus.Running;
            }
        }

        async ValueTask ExecuteTaskStageAsync(
            TaskFlowTemplateModel taskFlowTemplate,
            TaskFlowExecutionInstanceModel taskFlowExecutionInstance,
            TaskFlowStageExecutionInstance taskFlowStageExecutionInstance,
            CancellationToken cancellationToken = default)
        {
            foreach (var taskGroupExecutionInstance in taskFlowStageExecutionInstance.TaskGroups)
            {
                if (taskGroupExecutionInstance.IsTerminatedStatus())
                {
                    continue;
                }
                await ExecuteTaskGroupAsync(
                    taskFlowTemplate,
                    taskFlowExecutionInstance,
                    taskFlowStageExecutionInstance,
                    taskGroupExecutionInstance,
                    cancellationToken);
            }
            if (taskFlowStageExecutionInstance.TaskGroups.Count == 0)
            {
                taskFlowStageExecutionInstance.Status = TaskExecutionStatus.Finished;
            }
            else if (taskFlowStageExecutionInstance.TaskGroups.All(x => x.Status == TaskExecutionStatus.Finished))
            {
                taskFlowStageExecutionInstance.Status = TaskExecutionStatus.Finished;
            }
            else if (taskFlowStageExecutionInstance.TaskGroups.Any(x => x.Status != TaskExecutionStatus.Finished))
            {
                taskFlowStageExecutionInstance.Status = TaskExecutionStatus.Running;
            }
        }

        async ValueTask ExecuteTaskGroupAsync(
            TaskFlowTemplateModel taskFlowTemplate,
            TaskFlowExecutionInstanceModel taskFlowExecutionInstance,
            TaskFlowStageExecutionInstance taskFlowStageExecutionInstance,
            TaskFlowGroupExecutionInstance taskFlowGroupExecutionInstance,
            CancellationToken cancellationToken = default)
        {
            if (taskFlowGroupExecutionInstance.IsTerminatedStatus())
            {
                return;
            }
            foreach (var taskFlowTaskExecutionInstance in taskFlowGroupExecutionInstance.Tasks)
            {
                await ExecuteTaskAsync(
                    taskFlowTemplate,
                    taskFlowExecutionInstance,
                    taskFlowStageExecutionInstance,
                    taskFlowGroupExecutionInstance,
                    taskFlowTaskExecutionInstance,
                    cancellationToken);
                if (taskFlowTaskExecutionInstance.Status == TaskExecutionStatus.Finished)
                {
                    continue;
                }
                break;
            }
            if (taskFlowGroupExecutionInstance.Tasks.Count == 0)
            {
                taskFlowGroupExecutionInstance.Status = TaskExecutionStatus.Finished;
            }
            else if (taskFlowGroupExecutionInstance.Tasks.All(x => x.Status == TaskExecutionStatus.Finished))
            {
                taskFlowGroupExecutionInstance.Status = TaskExecutionStatus.Finished;
            }
            else if (taskFlowGroupExecutionInstance.Tasks.Any(x => x.Status != TaskExecutionStatus.Finished))
            {
                taskFlowGroupExecutionInstance.Status = TaskExecutionStatus.Running;
            }
        }

        async ValueTask ExecuteTaskAsync(
            TaskFlowTemplateModel taskFlowTemplate,
            TaskFlowExecutionInstanceModel taskFlowExecutionInstance,
            TaskFlowStageExecutionInstance taskFlowStageExecutionInstance,
            TaskFlowGroupExecutionInstance taskFlowGroupExecutionInstance,
            TaskFlowTaskExecutionInstance taskFlowTaskExecutionInstance,
            CancellationToken cancellationToken = default)
        {
            var taskFlowTaskTemplate = taskFlowTemplate
                  .Value.FindStageTemplate(taskFlowStageExecutionInstance.TaskFlowStageTemplateId)
                  ?.FindGroupTemplate(taskFlowGroupExecutionInstance.TaskFlowGroupTemplateId)
                  ?.FindTaskTemplate(taskFlowTaskExecutionInstance.TaskFlowTaskTemplateId);
            if (taskFlowTaskTemplate == null)
            {
                throw new InvalidOperationException();
            }
            if (taskFlowTaskTemplate.TemplateType == TaskFlowTaskTemplateType.TriggerTask)
            {
                taskFlowTaskExecutionInstance.Status = TaskExecutionStatus.Finished;
            }
            else if (taskFlowTaskTemplate.TemplateType == TaskFlowTaskTemplateType.RemoteNodeTask)
            {
                switch (taskFlowTaskExecutionInstance.Status)
                {
                    case TaskExecutionStatus.Unknown:
                        var fireInstanceId = $"TaskFlow_{Guid.NewGuid()}";
                        await _taskActivateServiceParametersBatchQueue.SendAsync(new TaskActivateServiceParameters(new FireTaskParameters
                        {
                            FireTimeUtc = DateTime.UtcNow,
                            TriggerSource = TriggerSource.Manual,
                            FireInstanceId = fireInstanceId,
                            TaskDefinitionId = taskFlowTaskExecutionInstance.TaskDefinitionId,
                            ScheduledFireTimeUtc = DateTime.UtcNow,
                            TaskFlowTaskKey = new TaskFlowTaskKey(
                                 taskFlowExecutionInstance.Value.TaskFlowTemplateId,
                                 taskFlowExecutionInstance.Value.Id,
                                 taskFlowStageExecutionInstance.Id,
                                 taskFlowGroupExecutionInstance.Id,
                                 taskFlowTaskExecutionInstance.Id)
                        }), cancellationToken);

                        break;
                    case TaskExecutionStatus.Triggered:
                    case TaskExecutionStatus.Pendding:
                    case TaskExecutionStatus.Started:
                    case TaskExecutionStatus.Running:
                        break;
                    case TaskExecutionStatus.Finished:
                        break;
                    case TaskExecutionStatus.Failed:
                    case TaskExecutionStatus.Cancelled:
                    case TaskExecutionStatus.PenddingTimeout:
                        break;
                    case TaskExecutionStatus.MaxCount:
                        break;
                    default:
                        break;
                }
            }

            await ValueTask.CompletedTask;
        }

    }
}
