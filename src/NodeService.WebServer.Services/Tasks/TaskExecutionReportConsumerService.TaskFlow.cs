using Grpc.Core;
using System.Collections.Immutable;

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
            IEnumerable<TaskActivationRecordModel> taskActivationRecordList,
            CancellationToken cancellationToken = default)
        {
            foreach (var taskActiveRecordGroup in taskActivationRecordList.GroupBy(GetTaskFlowInstanceId))
            {
                if (taskActiveRecordGroup.Key == null)
                {
                    continue;
                }
                var taskFlowInstanceId = taskActiveRecordGroup.Key;

                await _taskFlowExecutor.ExecuteAsync(taskFlowInstanceId, [.. taskActiveRecordGroup], cancellationToken);
            }
        }
    }
}
