namespace NodeService.WebServer.Services.Tasks;

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

        foreach (var taskExecutionNodeInfo in TaskActivationRecord.TaskExecutionNodeList)
        {
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

        var totalCount = TaskActivationRecord.TotalCount;
        var failedCount = TaskActivationRecord.FailedCount;
        var runningCount = TaskActivationRecord.RunningCount;
        var canceledCount = TaskActivationRecord.CancelledCount;
        var triggeredCount = TaskActivationRecord.TriggeredCount;
        var penddingCount = TaskActivationRecord.PenddingCount;
        var pennddingTimeoutCount = TaskActivationRecord.PenddingTimeoutCount;
        var finishedCount = TaskActivationRecord.FinishedCount;


        if (finishedCount == totalCount)
        {
            TaskActivationRecord.Status = TaskExecutionStatus.Finished;
        }
        else if (failedCount == totalCount)
        {
            TaskActivationRecord.Status = TaskExecutionStatus.Failed;
        }
        else if (canceledCount == totalCount)
        {
            TaskActivationRecord.Status = TaskExecutionStatus.Cancelled;
        }
        else if (pennddingTimeoutCount == totalCount)
        {
            TaskActivationRecord.Status = TaskExecutionStatus.PenddingTimeout;
        }
        else if (runningCount > 0)
        {
            TaskActivationRecord.Status = TaskExecutionStatus.Running;
        }
        else if (runningCount == 0 && (failedCount > 0 || canceledCount > 0 || pennddingTimeoutCount > 0))
        {
            TaskActivationRecord.Status = TaskExecutionStatus.Failed;
        }
        else if (triggeredCount == totalCount)
        {
            TaskActivationRecord.Status = TaskExecutionStatus.Triggered;
        }
        else if (penddingCount == totalCount)
        {
            TaskActivationRecord.Status = TaskExecutionStatus.Pendding;
        }

        TaskActivationRecord.TaskExecutionNodeList = [.. TaskActivationRecord.TaskExecutionNodeList.Select(x => x with { })];
        await ValueTask.CompletedTask;
    }

}