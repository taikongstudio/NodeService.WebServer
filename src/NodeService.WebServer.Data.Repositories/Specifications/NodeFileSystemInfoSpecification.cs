using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Repositories.Specifications
{
    public class NodeFileSystemInfoSpecification : SelectSpecification<NodeFileSystemInfoModel,string>
    {
        public NodeFileSystemInfoSpecification(string nodeInfoId, DataFilterCollection<string> idFilters)
        {
            Query.Where(x => x.NodeInfoId == nodeInfoId);
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

        public NodeFileSystemInfoSpecification(string nodeName, string keywords)
        {
            if (nodeName != null)
            {
                Query.Where(x => x.NodeInfoId.Contains(nodeName));
            }
            if (!string.IsNullOrEmpty(keywords))
            {
                Query.Where(x => x.Path.Contains(keywords));
            }
        }

    }
}
