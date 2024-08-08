using Grpc.Core;
using System.Collections.Immutable;
using NodeService.Infrastructure.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;

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

        private async ValueTask<ListQueryResult<TaskExecutionInstanceModel>> QueryTaskExecutionInstanceListAsync(
            int pageIndex,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            await using var taskExecutionInstanceRepo = await _taskExecutionInstanceRepoFactory.CreateRepositoryAsync();
            var listQueryResult = await taskExecutionInstanceRepo.PaginationQueryAsync(
                new TaskExecutionInstanceListSpecification(
                    DataFilterCollection<TaskExecutionStatus>.Includes(
                    [
                        TaskExecutionStatus.Triggered,
                        TaskExecutionStatus.Started,
                        TaskExecutionStatus.Running
                    ]),
                    false),
                new PaginationInfo(pageIndex, pageSize),
                cancellationToken);
            return listQueryResult;
        }
    }
}
