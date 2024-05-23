using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.QueryOptimize
{
    public class ClientUpdateBatchQueueOperationParameters
    {
        public ClientUpdateBatchQueueOperationParameters(string name, string ipAddress)
        {
            Name = name;
            IpAddress = ipAddress;
        }

        public string Name { get; private set; }
        public string IpAddress { get; private set; }
    }
}
