using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Repositories.Specifications
{
    public class NodeProfileSpecification : Specification<NodeProfileModel>
    {
        public NodeProfileSpecification(string name)
        {
            Query.Where(x => x.Name == name);
            Query.OrderByDescending(x => x.ServerUpdateTimeUtc);
        }
    }
}
