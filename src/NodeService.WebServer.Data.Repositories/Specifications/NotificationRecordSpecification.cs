using NodeService.WebServer.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Repositories.Specifications
{
    public class NotificationRecordSpecification : Specification<NotificationRecordModel>
    {
        public NotificationRecordSpecification(
            string? keywords,
            IEnumerable<SortDescription>? sortDescriptions = null)
        {
            if (!string.IsNullOrEmpty(keywords))
            {
                Query.Where(x => x.Name.Contains(keywords));
            }
            Query.OrderByDescending(x => x.CreationDateTime);
            if (sortDescriptions != null && sortDescriptions.Any())
            {
                Query.SortBy(sortDescriptions);
            }
        }
    }
}
