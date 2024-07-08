using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Repositories.Specifications;

public class DataQualityStatisticsSelectSpecification<TProject> : SelectSpecification<DataQualityNodeStatisticsRecordModel, TProject>
    where TProject : class
{
    public DataQualityStatisticsSelectSpecification(string nodeId, DateTime dateTime)
    {
        Query.Where(x => x.Id == nodeId);
        Query.Where(x => x.CreationDateTime == dateTime);
    }

    public DataQualityStatisticsSelectSpecification(
        DateTime beginDateTime,
        DateTime endDateTime,
        DataFilterCollection<string> nodeIdFilters = default)
    {
        Query.Where(x => x.CreationDateTime >= beginDateTime && x.CreationDateTime <= endDateTime);
        if (nodeIdFilters.HasValue)
        {
            if (nodeIdFilters.FilterType == DataFilterTypes.Include)
                Query.Where(x => nodeIdFilters.Items.Contains(x.NodeId));
            else if (nodeIdFilters.FilterType == DataFilterTypes.Exclude)
                Query.Where(x => !nodeIdFilters.Items.Contains(x.NodeId));
        }
    }
}