using NodeService.WebServer.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Repositories.Specifications
{
    public class CommonConfigSpecification<T> : Specification<T> where T : ConfigurationModel
    {
        public CommonConfigSpecification(
            string? keywords,
            IEnumerable<SortDescription>? sortDescriptions = null)
        {
            if (!string.IsNullOrEmpty(keywords))
            {
                Query.Where(x => x.Name.Contains(keywords));
            }
            if (sortDescriptions != null && sortDescriptions.Any())
            {
                Query.SortBy(sortDescriptions);
            }
        }
    }
}
