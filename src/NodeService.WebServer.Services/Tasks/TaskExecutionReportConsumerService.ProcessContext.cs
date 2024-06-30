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


        static (TaskFlowTaskKey TaskFlowTaskKey, string? FireInstanceId) GetFireInstanceId(TaskExecutionReportProcessContext processContext)
        {
            if (processContext.TaskExecutionInstance == null)
            {
                return default;
            }
            return (processContext.TaskExecutionInstance.GetTaskFlowTaskKey(), processContext.TaskExecutionInstance.FireInstanceId);
        }

        static string? GetTaskFlowFireInstanceId(TaskActivationRecordModel  taskActivationRecord)
        {
            if (taskActivationRecord.TaskDefinitionId == null)
            {
                return null;
            }
            return taskActivationRecord.TaskFlowFireInstanceId;
        }

        async Task ProcessContextAsync(CancellationToken cancellationToken = default)
        {
            await foreach (var array in _reportProcessContextBatchQueue.ReceiveAllAsync(cancellationToken))
            {
                await ProcessContextAsync(array.SelectMany(static x => x.Where(static y => y.TaskExecutionInstance != null)), cancellationToken);
            }
        }

        async ValueTask ProcessContextAsync(
            IEnumerable<TaskExecutionReportProcessContext> contexts,
            CancellationToken cancellationToken = default)
        {

            try
            {
                using var taskActivationRecordRepo = _taskActivationRecordRepoFactory.CreateRepository();
                var taskActivationRecordList = new List<TaskActivationRecordModel>();
                var changedContexts = contexts.Where(x => x.StatusChanged || x.MessageChanged);
                foreach (var taskInstanceGroup in changedContexts.GroupBy(GetFireInstanceId))
                {

                    if (taskInstanceGroup.Key == default)
                    {
                        continue;
                    }

                    var taskActivationRecord = await taskActivationRecordRepo.GetByIdAsync(taskInstanceGroup.Key.FireInstanceId, cancellationToken);
                    if (taskActivationRecord == null)
                    {
                        continue;
                    }
                    var taskExecutionInstanceInfoList = taskInstanceGroup.Select(static x => new TaskExecutionInstanceInfo()
                    {
                        NodeInfoId = x.TaskExecutionInstance!.NodeInfoId,
                        TaskExecutionInstanceId = x.TaskExecutionInstance.Id,
                        Status = x.TaskExecutionInstance.Status,
                        Message = x.TaskExecutionInstance.Message
                    });
                    taskActivationRecord.Value.ResetCounters();
                    for (int i = 0; i < taskActivationRecord.TaskExecutionInstanceInfoList.Count; i++)
                    {
                        var info = taskActivationRecord.TaskExecutionInstanceInfoList[i];
                        var taskExecutionInstance = info.TaskExecutionInstanceId;
                        var newValue = taskExecutionInstanceInfoList.FirstOrDefault(x => x.TaskExecutionInstanceId == taskExecutionInstance);
                        if (newValue != default)
                        {
                            info.Status = newValue.Status;
                            info.Message = newValue.Message;
                            taskActivationRecord.TaskExecutionInstanceInfoList[i] = info;
                        }
                        switch (info.Status)
                        {
                            case TaskExecutionStatus.Unknown:
                                break;
                            case TaskExecutionStatus.Triggered:
                                taskActivationRecord.Value.TriggeredCount++;
                                break;
                            case TaskExecutionStatus.Pendding:
                                break;
                            case TaskExecutionStatus.Started:

                                break;
                            case TaskExecutionStatus.Running:
                                taskActivationRecord.Value.RunningCount++;
                                break;
                            case TaskExecutionStatus.Failed:
                                taskActivationRecord.Value.FailedCount++;
                                break;
                            case TaskExecutionStatus.Finished:
                                taskActivationRecord.Value.FinishedCount++;
                                break;
                            case TaskExecutionStatus.Cancelled:
                                taskActivationRecord.Value.CancelledCount++;
                                break;
                            case TaskExecutionStatus.PenddingTimeout:
                                taskActivationRecord.Value.PenddingTimeoutCount++;
                                break;
                            case TaskExecutionStatus.MaxCount:
                                break;
                            default:
                                break;
                        }
                    }

                    taskActivationRecord.TaskFlowTemplateId = taskInstanceGroup.Key.TaskFlowTaskKey.TaskFlowTemplateId;
                    taskActivationRecord.TaskFlowFireInstanceId = taskInstanceGroup.Key.TaskFlowTaskKey.TaskFlowInstanceId;
                    taskActivationRecord.TaskFlowStageId = taskInstanceGroup.Key.TaskFlowTaskKey.TaskFlowStageId;
                    taskActivationRecord.TaskFlowGroupId = taskInstanceGroup.Key.TaskFlowTaskKey.TaskFlowGroupId;
                    taskActivationRecord.TaskFlowTaskId = taskInstanceGroup.Key.TaskFlowTaskKey.TaskFlowTaskId;

                    if (taskActivationRecord.FinishedCount == taskActivationRecord.TotalCount)
                    {
                        taskActivationRecord.Status = TaskExecutionStatus.Finished;
                    }
                    else if (taskActivationRecord.TriggeredCount == taskActivationRecord.TotalCount)
                    {
                        taskActivationRecord.Status = TaskExecutionStatus.Triggered;
                    }
                    else if (taskActivationRecord.CancelledCount > 0)
                    {
                        taskActivationRecord.Status = TaskExecutionStatus.Cancelled;
                    }
                    else if (taskActivationRecord.FailedCount > 0)
                    {
                        taskActivationRecord.Status = TaskExecutionStatus.Failed;
                    }
                    else if (taskActivationRecord.PenddingTimeoutCount > 0)
                    {
                        taskActivationRecord.Status = TaskExecutionStatus.PenddingTimeout;
                    }
                    else if (taskActivationRecord.RunningCount == taskActivationRecord.TotalCount)
                    {
                        taskActivationRecord.Status = TaskExecutionStatus.Running;
                    }
                    else if (taskActivationRecord.RunningCount + taskActivationRecord.FinishedCount == taskActivationRecord.TotalCount)
                    {
                        taskActivationRecord.Status = TaskExecutionStatus.Running;
                    }
                    taskActivationRecord.TaskExecutionInstanceInfoList = [.. taskActivationRecord.TaskExecutionInstanceInfoList];

                    taskActivationRecordList.Add(taskActivationRecord);
                }
                foreach (var array in taskActivationRecordList.Chunk(10))
                {
                    await taskActivationRecordRepo.UpdateRangeAsync(array, cancellationToken);
                    int changes = taskActivationRecordRepo.LastChangesCount;
                }
                taskActivationRecordList = taskActivationRecordList.Where(x => x.GetTaskFlowTaskKey() != default).ToList();
                if (taskActivationRecordList.Count > 0)
                {
                    await ProcessTaskFlowActiveRecordListAsync(taskActivationRecordList, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
            finally
            {

            }

        }

        async ValueTask ProcessTaskFlowActiveRecordListAsync(
            IList<TaskActivationRecordModel> taskActivationRecordList,
            CancellationToken cancellationToken = default)
        {
            if (taskActivationRecordList.Count == 0)
            {
                return;
            }
            using var taskFlowExecutionInstanceRepo = _taskFlowExecutionInstanceRepoFactory.CreateRepository();
            foreach (var taskActiveRecordGroup in taskActivationRecordList.GroupBy(GetTaskFlowFireInstanceId))
            {
                if (taskActiveRecordGroup.Key == null)
                {
                    continue;
                }
                var taskFlowFireInstanceId = taskActiveRecordGroup.Key;
                var taskFlowExecutionInstance = await taskFlowExecutionInstanceRepo.GetByIdAsync(taskFlowFireInstanceId, cancellationToken);
                if (taskFlowExecutionInstance == null)
                {
                    continue;
                }
                foreach (var activationRecordModel in taskActiveRecordGroup)
                {
                    var taskStage = taskFlowExecutionInstance.TaskStages.FirstOrDefault(x => x.Id == activationRecordModel.TaskFlowStageId);
                    if (taskStage == null)
                    {
                        continue;
                    }
                    var taskGroup = taskStage.TaskGroups.FirstOrDefault(x => x.Id == activationRecordModel.TaskFlowGroupId);
                    if (taskGroup == null)
                    {
                        continue;
                    }
                    var task = taskGroup.Tasks.FirstOrDefault(x => x.Id == activationRecordModel.TaskFlowTaskId);
                    if (task == null)
                    {
                        continue;
                    }
                    task.Status = activationRecordModel.Status;
                }
                await taskFlowExecutionInstanceRepo.UpdateAsync(taskFlowExecutionInstance, cancellationToken);
            }
        }
    }
}
