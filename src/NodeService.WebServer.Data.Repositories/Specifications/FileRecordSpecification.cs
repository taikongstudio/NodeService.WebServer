using NodeService.WebServer.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace NodeService.WebServer.Data.Repositories.Specifications
{
    public class FileRecordSpecification : Specification<FileRecordModel>
    {
        public FileRecordSpecification(
            string nodeId,
            string? keywords,
            IEnumerable<SortDescription>? sortDescriptions)
        {
            if (!string.IsNullOrEmpty(nodeId))
            {
                Query.Where(x => x.Id == nodeId);
            }
            if (!string.IsNullOrEmpty(keywords))
            {
                Query.Where(x => x.OriginalFileName.Contains(keywords));
            }
            if (sortDescriptions != null && sortDescriptions.Any())
            {
                Query.SortBy(sortDescriptions);
            }
        }
    }
}
