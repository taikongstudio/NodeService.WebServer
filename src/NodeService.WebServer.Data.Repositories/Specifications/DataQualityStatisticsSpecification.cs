using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Repositories.Specifications
{
    public class DataQualityStatisticsSpecification : Specification<DataQualityNodeStatisticsRecordModel>
    {
        public DataQualityStatisticsSpecification(string nodeId, DateTime dateTime)
        {
            Query.Where(x => x.Id == nodeId);
            Query.Where(x => x.CreationDateTime == dateTime);
        }

        public DataQualityStatisticsSpecification(
            DateTime beginDateTime,
            DateTime endDateTime,
            DataFilterCollection<string> idFilters = default)
        {
            Query.Where(x => x.CreationDateTime >= beginDateTime && x.CreationDateTime <= endDateTime);
            if (idFilters.HasValue)
            {
                if (idFilters.FilterType == DataFilterTypes.Include)
                {
                    Query.Where(x => idFilters.Items.Contains(x.NodeId));
                }
                else if (idFilters.FilterType == DataFilterTypes.Exclude)
                {
                    Query.Where(x => !idFilters.Items.Contains(x.NodeId));
                }
            }
        }
    }
}
