using Microsoft.Extensions.DependencyInjection;
using NodeService.WebServer.Services.NodeSessions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.TaskSchedule.Jobs
{
    public class FireNodeHeathyCheckJob : JobBase
    {
        public FireNodeHeathyCheckJob(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public override async Task Execute(IJobExecutionContext context)
        {
            var queue = ServiceProvider.GetService<IAsyncQueue<NodeHealthyCheckFireEvent>>();
            await queue.EnqueueAsync(new NodeHealthyCheckFireEvent()
            {

            });
        }
    }
}
