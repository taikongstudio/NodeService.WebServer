using NodeService.WebServer.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Repositories.Specifications
{
    public class TaskFlowExecutionInstanceSpecification : SelectSpecification<TaskFlowExecutionInstanceModel, string>
    {
        public TaskFlowExecutionInstanceSpecification(
            string keywords,
            TaskFlowExecutionStatus taskFlowExecutionStatus,
            DateTime? startTime,
            DateTime? endTime,
            DataFilterCollection<string> taskFlowTemplateIdFilter,
            IEnumerable<SortDescription> sortDescriptions = null)
        {
            if (taskFlowExecutionStatus != TaskFlowExecutionStatus.Unknown)
            {
                Query.Where(x => x.Status == taskFlowExecutionStatus);
            }
            if (!string.IsNullOrEmpty(keywords))
            {
                Query.Where(x => x.Name.Contains(keywords));
            }
            if (startTime != null && startTime.HasValue && endTime != null && endTime.HasValue)
                Query.Where(x => x.CreationDateTime >= startTime && x.CreationDateTime <= endTime);
            else if (startTime == null && endTime != null && endTime.HasValue)
                Query.Where(x => x.CreationDateTime <= endTime);
            else if (startTime != null && startTime.HasValue && endTime == null)
                Query.Where(x => x.CreationDateTime >= startTime);
            if (taskFlowTemplateIdFilter.HasValue)
            {
                if (taskFlowTemplateIdFilter.FilterType == DataFilterTypes.Include)
                {
                    Query.Where(x => taskFlowTemplateIdFilter.Items.Contains(x.TaskFlowTemplateId));
                }
                else if (taskFlowTemplateIdFilter.FilterType == DataFilterTypes.Exclude)
                {
                    Query.Where(x => !taskFlowTemplateIdFilter.Items.Contains(x.TaskFlowTemplateId));
                }
            }
            if (sortDescriptions != null && sortDescriptions.Any())
            {
                Query.SortBy(sortDescriptions);
            }
        }

        protected TaskFlowExecutionInstanceSpecification(string id)
        {
            Query.Where(x => x.Id == id);
        }
    }
}
