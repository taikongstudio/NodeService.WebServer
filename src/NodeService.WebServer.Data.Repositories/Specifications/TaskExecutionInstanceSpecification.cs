using NodeService.WebServer.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace NodeService.WebServer.Data.Repositories.Specifications
{
    public class TaskExecutionInstanceSpecification : Specification<JobExecutionInstanceModel>
    {
        public TaskExecutionInstanceSpecification(
            string? keywords,
            JobExecutionStatus status,
            IEnumerable<string> nodeIdList,
            IEnumerable<string> taskDefinitionIdList,
            IEnumerable<string> taskExecutionIdList,
            IEnumerable<SortDescription>? sortDescriptions = null)
        {
            if (status!= JobExecutionStatus.Unknown)
            {
                Query.Where(x => x.Status == status);
            }
            if (!string.IsNullOrEmpty(keywords))
            {
                Query.Where(x => x.Name.Contains(keywords));
            }
            if (nodeIdList != null && nodeIdList.Any())
            {
                Query.Where(x => nodeIdList.Contains(x.NodeInfoId));
            }
            if (taskDefinitionIdList != null && taskDefinitionIdList.Any())
            {
                Query.Where(x => taskDefinitionIdList.Contains(x.JobScheduleConfigId));
            }
            if (taskExecutionIdList != null && taskExecutionIdList.Any())
            {
                Query.Where(x => taskExecutionIdList.Contains(x.Id));
            }
            if (sortDescriptions != null && sortDescriptions.Any())
            {
                Query.SortBy(sortDescriptions);
            }
        }

    }
}
