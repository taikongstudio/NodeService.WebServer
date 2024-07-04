using NodeService.WebServer.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Repositories.Specifications
{
    public class TaskActivationRecordSpecification : Specification<TaskActivationRecordModel>
    {
        public TaskActivationRecordSpecification(
            string? keywords,
            TaskExecutionStatus status,
            DateTime? startTime,
            DateTime? endTime,
            DataFilterCollection<string> taskDefinitionIdList = default,
            IEnumerable<SortDescription>? sortDescriptions = null)
        {
            if (!string.IsNullOrEmpty(keywords))
            {
                Query.Where(x => x.Name.Contains(keywords));
            }
            if (status != TaskExecutionStatus.Unknown)
            {
                Query.Where(x => x.Status == status);
            }
            Query.Where(x => x.CreationDateTime >= startTime && x.CreationDateTime <= endTime);
            if (taskDefinitionIdList.HasValue)
            {
                if (taskDefinitionIdList.FilterType == DataFilterTypes.Include)
                {
                    Query.Where(x => taskDefinitionIdList.Items.Contains(x.TaskDefinitionId));
                }
                else if (taskDefinitionIdList.FilterType == DataFilterTypes.Exclude)
                {
                    Query.Where(x => !taskDefinitionIdList.Items.Contains(x.TaskDefinitionId));
                }
            }
            if (sortDescriptions != null && sortDescriptions.Any())
            {
                Query.SortBy(sortDescriptions);
            }
        }
    }
}
