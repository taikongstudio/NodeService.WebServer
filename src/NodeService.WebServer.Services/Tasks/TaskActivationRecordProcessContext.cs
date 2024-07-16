namespace NodeService.WebServer.Services.Tasks;

public partial class TaskExecutionReportConsumerService
{
    class TaskActivationRecordProcessContext
    {
        public string FireInstanceId { get; init; }

        public TaskActivationRecordModel TaskActivationRecord { get; init; }

        public IEnumerable<TaskExecutionInstanceProcessContext> TaskExecutionInstanceProcessContexts { get; init; } = [];

        public async ValueTask ProcessAsync(CancellationToken cancellationToken = default)
        {
            if (TaskExecutionInstanceProcessContexts == null)
            {
                return;
            }

            var taskExecutionInstanceInfoList = TaskExecutionInstanceProcessContexts.Select(static x => new TaskExecutionInstanceInfo()
            {
                TaskExecutionInstanceId = x.TaskExecutionInstance.Id,
                Status = x.TaskExecutionInstance.Status,
                Message = x.TaskExecutionInstance.Message
            });
            TaskActivationRecord.Value.ResetCounters();
            for (int i = 0; i < TaskActivationRecord.TaskExecutionNodeList.Count; i++)
            {
                var taskExecutionNodeInfo = TaskActivationRecord.TaskExecutionNodeList[i];
                var nodeInfoId = taskExecutionNodeInfo.NodeInfoId;
                TaskExecutionInstanceModel? newValue = null;
                foreach (var processContext in TaskExecutionInstanceProcessContexts)
                {
                    if (processContext.TaskExecutionInstance == null)
                    {
                        continue;
                    }
                    if (processContext.TaskExecutionInstance.NodeInfoId == nodeInfoId)
                    {
                        newValue = processContext.TaskExecutionInstance;
                        break;
                    }
                }
                if (newValue != null)
                {
                    taskExecutionNodeInfo.AddOrUpdateInstance(newValue);
                }
                taskExecutionNodeInfo.UpdateCounters();
                TaskActivationRecord.TaskExecutionNodeList[i] = taskExecutionNodeInfo;
                switch (taskExecutionNodeInfo.Status)
                {
                    case TaskExecutionStatus.Unknown:
                        break;
                    case TaskExecutionStatus.Triggered:
                        TaskActivationRecord.TriggeredCount++;
                        break;
                    case TaskExecutionStatus.Pendding:
                        TaskActivationRecord.PenddingCount++;
                        break;
                    case TaskExecutionStatus.Started:
                        TaskActivationRecord.StartedCount++;
                        break;
                    case TaskExecutionStatus.Running:
                        TaskActivationRecord.RunningCount++;
                        break;
                    case TaskExecutionStatus.Failed:
                        TaskActivationRecord.FailedCount++;
                        break;
                    case TaskExecutionStatus.Finished:
                        TaskActivationRecord.FinishedCount++;
                        break;
                    case TaskExecutionStatus.Cancelled:
                        TaskActivationRecord.CancelledCount++;
                        break;
                    case TaskExecutionStatus.PenddingTimeout:
                        TaskActivationRecord.PenddingTimeoutCount++;
                        break;
                    case TaskExecutionStatus.MaxCount:
                        break;
                    default:
                        break;
                }
            }

            if (TaskActivationRecord.FinishedCount == TaskActivationRecord.TotalCount)
            {
                TaskActivationRecord.Status = TaskExecutionStatus.Finished;
            }
            else if (TaskActivationRecord.CancelledCount > 0)
            {
                TaskActivationRecord.Status = TaskExecutionStatus.Cancelled;
            }
            else if (TaskActivationRecord.FailedCount > 0)
            {
                TaskActivationRecord.Status = TaskExecutionStatus.Failed;
            }
            else if (TaskActivationRecord.PenddingTimeoutCount > 0)
            {
                TaskActivationRecord.Status = TaskExecutionStatus.PenddingTimeout;
            }
            else if (TaskActivationRecord.RunningCount > 0)
            {
                TaskActivationRecord.Status = TaskExecutionStatus.Running;
            }
            else if (TaskActivationRecord.PenddingCount == TaskActivationRecord.TotalCount)
            {
                TaskActivationRecord.Status = TaskExecutionStatus.Pendding;
            }
            else if (TaskActivationRecord.TriggeredCount == TaskActivationRecord.TotalCount)
            {
                TaskActivationRecord.Status = TaskExecutionStatus.Triggered;
            }
            TaskActivationRecord.TaskExecutionNodeList = [.. TaskActivationRecord.TaskExecutionNodeList];
            await ValueTask.CompletedTask;
        }

    }
}