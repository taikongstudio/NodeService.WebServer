using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Repositories.Specifications
{
    public class TaskFlowTemplateSpecification : SelectSpecification<TaskFlowTemplateModel, string>
    {
        public TaskFlowTemplateSpecification(DataFilterCollection<string> idFilters)
        {
            if (idFilters.HasValue)
            {
                if (idFilters.FilterType == DataFilterTypes.Include)
                {
                    Query.Where(x => idFilters.Items.Contains(x.Id));
                }
                else if (idFilters.FilterType == DataFilterTypes.Exclude)
                {
                    Query.Where(x => !idFilters.Items.Contains(x.Id));
                }
            }
        }
    }
}
