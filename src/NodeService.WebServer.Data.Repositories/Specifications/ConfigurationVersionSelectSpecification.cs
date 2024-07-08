using NodeService.WebServer.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Repositories.Specifications
{
    public class ConfigurationVersionSelectSpecification<TProjection> : SelectSpecification<ConfigurationVersionRecordModel, TProjection>
        where TProjection : class
    {
        public ConfigurationVersionSelectSpecification(string id, IEnumerable<SortDescription>? sortDescriptions = null)
        {
            Query.Where(x => x.ConfigurationId == id);
            if (sortDescriptions != null && sortDescriptions.Any())
            {
                Query.SortBy(sortDescriptions);
            }
        }

        public ConfigurationVersionSelectSpecification(string id, int version)
        {
            Query.Where(x => x.ConfigurationId == id && x.Version == version);
        }
    }
}
