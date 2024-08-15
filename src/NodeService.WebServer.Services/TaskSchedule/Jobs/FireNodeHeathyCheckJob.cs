using Microsoft.Extensions.DependencyInjection;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.NodeSessions;

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
            var webServerCounter = ServiceProvider.GetService<WebServerCounter>();
            webServerCounter.FireNodeHeathyCheckJobEnqueueCount.Value++;
            await queue.EnqueueAsync(new NodeHealthyCheckFireEvent()
            {

            });
        }
    }
}
