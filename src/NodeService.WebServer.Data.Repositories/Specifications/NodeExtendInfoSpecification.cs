using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Repositories.Specifications
{
    public class NodeExtendInfoSpecification : Specification<NodeExtendInfoModel>
    {
        public NodeExtendInfoSpecification(string nodeInfoId)
        {
            Query.Where(x => x.Id == nodeInfoId);
        }
    }
}
