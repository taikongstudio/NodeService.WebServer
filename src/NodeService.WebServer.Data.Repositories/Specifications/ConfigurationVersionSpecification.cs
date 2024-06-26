using NodeService.WebServer.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Repositories.Specifications
{
    public class ConfigurationVersionSpecification<T> : Specification<T> where T : ConfigurationVersionRecordModel
    {
        public ConfigurationVersionSpecification(string id, IEnumerable<SortDescription> sortDescriptions = null)
        {
            Query.Where(x => x.ConfigurationId == id);
            if (sortDescriptions != null && sortDescriptions.Any())
            {
                Query.SortBy(sortDescriptions);
            }
        }

        public ConfigurationVersionSpecification(string id, int version)
        {
            Query.Where(x => x.ConfigurationId == id && x.Version == version);
        }
    }
}
