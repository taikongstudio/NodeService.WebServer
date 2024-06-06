using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Repositories.Specifications
{
    public class TaskLogSpecification : Specification<TaskLogModel>
    {
        public TaskLogSpecification(string id, bool loadLogEntries = false)
        {
            Query.Where(x => x.Name == id);
            Query.OrderByDescending(x => x.PageIndex);
            if (!loadLogEntries)
            {
                Query.Include(x => x.Value, false);
            }
        }

        public TaskLogSpecification(string id, int pageIndex)
        {
            Query.Where(x => x.Name == id && pageIndex == x.PageIndex);
            Query.OrderBy(x => x.PageIndex);
        }

        public TaskLogSpecification(string id, int pageIndex, int pageCount)
        {
            Query.Where(x => x.Name == id && x.PageIndex >= pageIndex && x.PageIndex < pageIndex + pageCount);
            Query.OrderBy(x => x.PageIndex);
        }
    }
}
