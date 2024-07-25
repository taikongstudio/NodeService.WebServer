using NodeService.Infrastructure.DataModels;
using NodeService.WebServer.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
                    var status = taskFlowTaskExecutionInstance.Status;
                    if (status != activationRecord.Status)
                    {
                        status = activationRecord.Status;
                        switch (status)
                        {
                            case TaskExecutionStatus.Unknown:
                                break;
                            case TaskExecutionStatus.Triggered:
                                break;
                            case TaskExecutionStatus.Pendding:
                                break;
                            case TaskExecutionStatus.Started:
                                break;
                            case TaskExecutionStatus.Running:
                                break;
                            case TaskExecutionStatus.Failed:
                                break;
                            case TaskExecutionStatus.Finished:
                                break;
                            case TaskExecutionStatus.Cancelled:
                                break;
                            case TaskExecutionStatus.PenddingTimeout:
                                break;
                            case TaskExecutionStatus.MaxCount:
                                break;
                            default:
                                break;
                        }
                    }
                    taskFlowTaskExecutionInstance.Status = status;
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
