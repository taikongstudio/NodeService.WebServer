using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Repositories.Specifications
{
    public class TaskLogSpecification : Specification<TaskLogModel>
    {

        public TaskLogSpecification(string id, int pageIndex)
        {
            var key = $"{id}_{pageIndex}";
            Query.Where(x => x.Id == key && x.PageIndex == pageIndex);
            Query.OrderBy(x => x.PageIndex);
        }

        public TaskLogSpecification(string id, int pageIndex, int pageCount)
        {
            Query.Where(x => x.Id.Contains(id) && x.PageIndex >= pageIndex && x.PageIndex < pageIndex + pageCount);
            Query.OrderBy(x => x.PageIndex);
        }
    }
}
