using Grpc.Core;

namespace NodeService.WebServer.Services.Tasks
{
    public partial class TaskExecutionReportConsumerService
    {
        static string? GetTaskFlowInstanceId(TaskActivationRecordModel taskActivationRecord)
        {
            if (taskActivationRecord.TaskDefinitionId == null)
            {
                return null;
            }
            return taskActivationRecord.TaskFlowInstanceId;
        }

        async ValueTask ProcessTaskFlowActiveRecordListAsync(
            List<TaskActivationRecordModel> taskActivationRecordList,
            CancellationToken cancellationToken = default)
        {
            if (taskActivationRecordList.Count == 0)
            {
                return;
            }
            await using var taskFlowExecutionInstanceRepo = await _taskFlowExecutionInstanceRepoFactory.CreateRepositoryAsync();
            List<TaskFlowExecutionInstanceModel> taskFlowExecutionInstanceList = [];
            foreach (var taskActiveRecordGroup in taskActivationRecordList.GroupBy(GetTaskFlowInstanceId))
            {
                if (taskActiveRecordGroup.Key == null)
                {
                    continue;
                }
                var taskFlowInstanceId = taskActiveRecordGroup.Key;
                var taskFlowExecutionInstance = await taskFlowExecutionInstanceRepo.GetByIdAsync(taskFlowInstanceId, cancellationToken);
                if (taskFlowExecutionInstance == null)
                {
                    continue;
                }
                foreach (var activationRecord in taskActiveRecordGroup)
                {
                    var taskStage = taskFlowExecutionInstance.TaskStages.FirstOrDefault(x => x.Id == activationRecord.TaskFlowStageId);
                    if (taskStage == null)
                    {
                        continue;
                    }
                    var taskGroup = taskStage.TaskGroups.FirstOrDefault(x => x.Id == activationRecord.TaskFlowGroupId);
                    if (taskGroup == null)
                    {
                        continue;
                    }
                    var taskFlowTaskExecutionInstance = taskGroup.Tasks.FirstOrDefault(x => x.Id == activationRecord.TaskFlowTaskId);
                    if (taskFlowTaskExecutionInstance == null)
                    {
                        continue;
                    }
                    switch (activationRecord.Status)
                    {
                        case TaskExecutionStatus.Unknown:
                            taskFlowTaskExecutionInstance.Status = TaskExecutionStatus.Unknown;
                            break;
                        case TaskExecutionStatus.Triggered:
                        case TaskExecutionStatus.Pendding:
                        case TaskExecutionStatus.Started:
                        case TaskExecutionStatus.Running:
                            taskFlowTaskExecutionInstance.Status = TaskExecutionStatus.Running;
                            break;
                        case TaskExecutionStatus.Failed:
                        case TaskExecutionStatus.PenddingTimeout:
                        case TaskExecutionStatus.Cancelled:
                            taskFlowTaskExecutionInstance.Status = activationRecord.Status;
                            break;
                        case TaskExecutionStatus.Finished:
                            taskFlowTaskExecutionInstance.Status = TaskExecutionStatus.Finished;
                            break;
                        case TaskExecutionStatus.MaxCount:
                            break;
                        default:
                            break;
                    }
                    taskFlowTaskExecutionInstance.TaskActiveRecordId = activationRecord.Id;
                }
                taskFlowExecutionInstance.Value = taskFlowExecutionInstance.Value with { };
                //taskFlowExecutionInstanceRepo.DbContext.Entry(taskFlowExecutionInstance).State = EntityState.Modified;
                taskFlowExecutionInstanceList.Add(taskFlowExecutionInstance);
            }

            foreach (var taskFlowExecutionInstance in taskFlowExecutionInstanceList)
            {
                await _taskFlowExecutor.ExecuteAsync(taskFlowExecutionInstance, cancellationToken);
            }
            foreach (var item in taskFlowExecutionInstanceList.Chunk(10))
            {
                await taskFlowExecutionInstanceRepo.UpdateRangeAsync(item, cancellationToken);
            }
        }
    }
}
