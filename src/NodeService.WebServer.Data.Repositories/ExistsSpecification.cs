using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Repositories
{
    public class ExistsSpecification<T>:Specification<T>
        where T : EntityBase
    {
        public ExistsSpecification(string id)
        {
            this.Query.Where(x => x.Id == id);
        }
    }
}
