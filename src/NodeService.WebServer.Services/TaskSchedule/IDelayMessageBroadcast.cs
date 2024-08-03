using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.TaskSchedule
{
    public interface IDelayMessageBroadcast
    {
        void AddHandler(string type, Func<KafkaDelayMessage, CancellationToken, ValueTask> action);

        void RemoveHandler(string type);

        ValueTask BroadcastAsync(KafkaDelayMessage kafkaDelayMessage, CancellationToken cancellationToken = default);
    }
}
