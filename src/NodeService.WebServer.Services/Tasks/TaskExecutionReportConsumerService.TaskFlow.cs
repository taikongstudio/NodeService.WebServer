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
        static string? GetTaskFlowInstanceId(TaskActivationRecordModel  taskActivationRecord)
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
                    taskFlowTaskExecutionInstance.Status = activationRecord.Status;
                    taskFlowTaskExecutionInstance.TaskActiveRecordId = activationRecord.Id;
                }
                taskFlowExecutionInstance.Value = taskFlowExecutionInstance.Value with { };
                //taskFlowExecutionInstanceRepo.DbContext.Entry(taskFlowExecutionInstance).State = EntityState.Modified;
                taskFlowExecutionInstanceList.Add(taskFlowExecutionInstance);
            }

            foreach (var array in taskFlowExecutionInstanceList.Chunk(10))
            {
                foreach (var taskFlowExeuctionInstance in array)
                {
                    await _taskFlowExecutor.ExecuteAsync(taskFlowExeuctionInstance, cancellationToken);
                }
                await taskFlowExecutionInstanceRepo.UpdateRangeAsync(array, cancellationToken);
            }
        }
    }
}
