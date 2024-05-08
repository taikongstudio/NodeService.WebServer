using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Models
{
    public class ExceptionCounter
    {
        readonly ConcurrentDictionary<string, int> _dict;

        public ExceptionCounter()
        {
            _dict = new ConcurrentDictionary<string, int>();
        }

        public void AddOrUpdate(Exception exception)
        {
            var ex = exception.ToString();
            _dict.AddOrUpdate(ex, 1, UpdateValueImpl);
        }

        int UpdateValueImpl(string key, int value)
        {
            return value + 1;
        }

        public IEnumerable<(string Exception, int Count)> GetStatistics()
        {
            foreach (var kv in _dict)
            {
                yield return (kv.Key, kv.Value);
            }
            yield break;
        }


    }
}
