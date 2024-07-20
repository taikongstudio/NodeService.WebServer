using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Models
{
    public class KafkaOptions
    {
        public string BrokerList { get; set; }

        public string TaskLogTopic { get; set; }

        public string ClientUpdateLogTopic { get; set; }
    }
}
