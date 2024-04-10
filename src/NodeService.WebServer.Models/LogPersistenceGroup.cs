using NodeService.Infrastructure.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Models
{
    public struct LogPersistenceGroup
    {
        public LogPersistenceGroup(string id)
        {
            this.Id = id;
        }

        public string Id { get; private set; }
        public List<IEnumerable<LogEntry>> EntriesList { get; private set; } = [];

        public int TotalEntiresCount { get; set; }
    }
}
