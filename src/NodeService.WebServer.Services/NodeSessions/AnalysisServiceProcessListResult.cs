using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.NodeSessions
{

    record struct AnalysisServiceProcessListResult
    {
        public AnalysisServiceProcessListResult()
        {
        }

        public IEnumerable<string> Usages { get; set; } = [];

        public List<ServiceProcessInfo> StatusChangeProcessList { get; set; } = [];
    }
}
