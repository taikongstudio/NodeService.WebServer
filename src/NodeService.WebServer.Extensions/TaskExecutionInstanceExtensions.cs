using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NodeService.WebServer.Extensions
{
    public static class TaskExecutionInstanceExtensions
    {
        public static bool CanRetry(this TaskExecutionInstanceModel taskExecutionInstance)
        {
            if (taskExecutionInstance == null)
            {
                return false;
            }
            return taskExecutionInstance.Status switch
            {
                TaskExecutionStatus.PenddingTimeout or TaskExecutionStatus.Failed => true,
                _ => false,
            };
        }

        public static TaskFlowTaskKey GetTaskFlowTaskKey(this TaskExecutionInstanceModel taskExecutionInstance)
        {
            if (taskExecutionInstance == null)
            {
                return default;
            }
            if (taskExecutionInstance.TaskFlowTemplateId == null
                ||
                taskExecutionInstance.TaskFlowInstanceId == null
                ||
                taskExecutionInstance.TaskFlowStageId == null
                ||
                taskExecutionInstance.TaskFlowGroupId == null
                ||
                taskExecutionInstance.TaskFlowTaskId == null)
            {
                return default;
            }
            return new TaskFlowTaskKey(
                taskExecutionInstance.TaskFlowTemplateId,
                taskExecutionInstance.TaskFlowInstanceId,
                taskExecutionInstance.TaskFlowStageId,
                taskExecutionInstance.TaskFlowGroupId,
                taskExecutionInstance.TaskFlowTaskId);
        }

        public static bool IsTerminatedStatus(this TaskExecutionInstanceModel taskExecutionInstance)
        {
            if (taskExecutionInstance == null)
            {
                return false;
            }
            return taskExecutionInstance.Status switch
            {
                TaskExecutionStatus.Failed
                or TaskExecutionStatus.Finished
                or TaskExecutionStatus.Cancelled
                or TaskExecutionStatus.PenddingTimeout => true,
                _ => false,
            };
        }

        private static TaskExecutionEventRequest ToEvent(this TaskExecutionInstanceModel taskExecutionInstance)
        {
            var req = new TaskExecutionEventRequest
            {
                RequestId = Guid.NewGuid().ToString()
            };
            req.Parameters.Add(nameof(TaskExecutionInstanceModel.Id), taskExecutionInstance.Id);
            req.Parameters.Add(nameof(TaskExecutionInstanceModel.FireInstanceId), taskExecutionInstance.FireInstanceId);
            if (taskExecutionInstance.PreviousFireTimeUtc != null)
                req.Parameters.Add(nameof(TaskExecutionInstanceModel.PreviousFireTimeUtc), taskExecutionInstance.PreviousFireTimeUtc?.ToString() ?? string.Empty);

            if (taskExecutionInstance.NextFireTimeUtc != null)
                req.Parameters.Add(nameof(TaskExecutionInstanceModel.NextFireTimeUtc), taskExecutionInstance.NextFireTimeUtc?.ToString() ?? string.Empty);

            req.Parameters.Add(nameof(TaskExecutionInstanceModel.ScheduledFireTimeUtc), taskExecutionInstance.ScheduledFireTimeUtc?.ToString() ?? string.Empty);

            return req;
        }

        public static TaskExecutionEventRequest ToTriggerEvent(
            this TaskExecutionInstanceModel taskExecutionInstance,
            TaskDefinitionModel taskDefinition,
            List<StringEntry>? envVars = default)
        {
            var req = taskExecutionInstance.ToEvent();
            req.Parameters.Add(nameof(TaskCreationParameters.TaskTypeDefinition), JsonSerializer.Serialize(taskDefinition));
            if (envVars == null)
                req.Parameters.Add(nameof(TaskCreationParameters.EnvironmentVariables),
                    JsonSerializer.Serialize<List<StringEntry>>([]));
            else
                req.Parameters.Add(nameof(TaskCreationParameters.EnvironmentVariables), JsonSerializer.Serialize(envVars));

            req.Parameters.Add("RequestType", "Trigger");
            return req;
        }

        public static TaskExecutionEventRequest ToReinvokeEvent(this TaskExecutionInstanceModel taskExecutionInstance)
        {
            var req = taskExecutionInstance.ToEvent();
            req.Parameters.Add("RequestType", "Reinvoke");
            return req;
        }

        public static TaskExecutionEventRequest ToCancelEvent(
            this TaskExecutionInstanceModel taskExecutionInstance,
            TaskCancellationParameters taskCancellationParameters)
        {
            var req = taskExecutionInstance.ToEvent();
            req.Parameters.Add("RequestType", "Cancel");
            req.Parameters.Add("CancellationId", Guid.NewGuid().ToString());
            req.Parameters.Add(nameof(taskCancellationParameters.Source), taskCancellationParameters.Source);
            req.Parameters.Add(nameof(taskCancellationParameters.Context), taskCancellationParameters.Context);
            return req;
        }
    }
}
