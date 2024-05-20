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

        public DataQualityStatisticsSpecification(DateTime beginDateTime, DateTime endDateTime)
        {
            Query.Where(x => x.CreationDateTime >= beginDateTime && x.CreationDateTime <= endDateTime);
        }
    }
}
