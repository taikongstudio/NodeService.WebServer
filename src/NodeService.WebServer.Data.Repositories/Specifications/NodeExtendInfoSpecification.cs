using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Repositories.Specifications
{
    public class NodeExtendInfoSpecification : Specification<NodeExtendInfoModel>
    {
        public NodeExtendInfoSpecification(DataFilterCollection<string> idList)
        {
            if (idList.HasValue)
            {
                if (idList.FilterType== DataFilterTypes.Include)
                {
                    Query.Where(x => idList.Items.Contains(x.Id));
                }
                else if (idList.FilterType== DataFilterTypes.Exclude)
                {
                    Query.Where(x => !idList.Items.Contains(x.Id));
                }
            }
        }

    }
}
