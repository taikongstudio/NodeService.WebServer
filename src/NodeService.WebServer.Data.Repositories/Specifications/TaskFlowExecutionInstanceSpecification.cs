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
            IEnumerable<SortDescription> sortDescriptions = null)
        {
            if (!string.IsNullOrEmpty(keywords))
            {
                Query.Where(x => x.Name == keywords);
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
