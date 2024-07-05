using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Repositories.Specifications
{
    public class NodeFileSystemInfoSpecification : Specification<NodeFileSystemInfoModel>
    {
        public NodeFileSystemInfoSpecification(DataFilterCollection<string> idFilters)
        {
            if (idFilters.HasValue)
            {
                Query.Where(x => idFilters.Items.Contains(x.Id));
            }
        }

        public NodeFileSystemInfoSpecification(string nodeName, string keywords)
        {
            if (nodeName != null)
            {
                Query.Where(x => x.NodeName.Contains(nodeName));
            }
            if (!string.IsNullOrEmpty(keywords))
            {
                Query.Where(x => x.Path.Contains(keywords));
            }
        }

    }
}
