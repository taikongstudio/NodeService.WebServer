using Microsoft.Extensions.DependencyInjection;
using NodeService.WebServer.Services.NodeSessions;
using NodeService.WebServer.Services.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.TaskSchedule.Jobs
{
    public class FireTaskObservationJob : JobBase
    {
        public FireTaskObservationJob(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public override async Task Execute(IJobExecutionContext context)
        {
            var queue = ServiceProvider.GetService<IAsyncQueue<TaskObservationEventKafkaConsumerFireEvent>>();
            await queue.EnqueueAsync(new TaskObservationEventKafkaConsumerFireEvent()
            {

            });
        }
    }
}
