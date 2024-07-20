using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.Counters
{
    public class ClientUpdateCounterEntry
    {
        public string ConfigurationId { get; set; }

        public string NodeName { get; set; }

        public IEnumerable<string> Messages { get; set; } = [];
    }

    public class ClientUpdateCounter
    {
        private record struct ClientUpdateKey
        {
            public string ConfigurationId { get; set; }
            public string NodeName { get; set; }
        }

        private ConcurrentDictionary<ClientUpdateKey, ConcurrentQueue<string>> _clientUpdateDict;

        public ClientUpdateCounter()
        {
            _clientUpdateDict = new ConcurrentDictionary<ClientUpdateKey, ConcurrentQueue<string>>();
        }

        public void Enquque(ClientUpdateLog parameters)
        {
            var key = new ClientUpdateKey()
            {
                ConfigurationId = parameters.ClientUpdateConfigId,
                NodeName = parameters.NodeName,
            };
            var queue = _clientUpdateDict.GetOrAdd(key, new ConcurrentQueue<string>());
            queue.Enqueue($"{DateTime.UtcNow} {parameters.CategoryName}");
            while (queue.Count > 1000)
            {
                queue.TryDequeue(out _);
            }
        }

        public IEnumerable<ClientUpdateCounterEntry> Dump()
        {
            foreach (var item in _clientUpdateDict)
            {
                yield return new ClientUpdateCounterEntry()
                {
                    ConfigurationId = item.Key.ConfigurationId,
                    NodeName = item.Key.NodeName,
                    Messages = [.. item.Value]
                };
            }
            yield break;
        }

    }
}
