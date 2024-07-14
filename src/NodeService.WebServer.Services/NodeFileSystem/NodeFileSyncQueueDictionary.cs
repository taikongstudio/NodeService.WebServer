using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public class NodeFileSyncQueueDictionary : ConcurrentDictionary<string, NodeFileSyncQueue>
    {
       
    }

}
