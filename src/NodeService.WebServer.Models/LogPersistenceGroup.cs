using NodeService.Infrastructure.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Models
{
    public class LogPersistenceGroup
    {
        public string Id { get; set; }
        public List<LogMessageEntry> LogMessageEntries { get; set; } = [];
    }
}
