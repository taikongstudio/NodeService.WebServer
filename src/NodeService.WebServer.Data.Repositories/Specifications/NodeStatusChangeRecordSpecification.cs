using NodeService.WebServer.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Repositories.Specifications
{
    public class NodeStatusChangeRecordSpecification : Specification<NodeStatusChangeRecordModel>
    {
        public NodeStatusChangeRecordSpecification(
            string? keywords,
            DateTime beginDateTime,
            DateTime endDateTime,
            DataFilterCollection<string> nodeIdFilters,
            IEnumerable<SortDescription>? sortDescriptions = null
            )
        {
            if (!string.IsNullOrEmpty(keywords))
            {
                Query.Where(x => x.Name.Contains(keywords));
            }
            Query.Where(x => x.DateTime >= beginDateTime && x.DateTime <= endDateTime);
            if (nodeIdFilters.HasValue)
            {
                if (nodeIdFilters.FilterType == DataFilterTypes.Include)
                {
                    Query.Where(x => nodeIdFilters.Items.Contains(x.NodeId));
                }
                else if (nodeIdFilters.FilterType == DataFilterTypes.Exclude)
                {
                    Query.Where(x => !nodeIdFilters.Items.Contains(x.NodeId));
                }
            }
            if (sortDescriptions != null)
            {
                Query.SortBy(sortDescriptions);
            }
        }

    }
}
