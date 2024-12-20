﻿using Microsoft.Extensions.DependencyInjection;
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
            if (Debugger.IsAttached) { return; }
            var queue = ServiceProvider.GetService<IAsyncQueue<NodeHealthyCheckFireEvent>>();
            var webServerCounter = ServiceProvider.GetService<WebServerCounter>();
            webServerCounter.Snapshot.FireNodeHeathyCheckJobEnqueueCount.Value++;
            await queue.EnqueueAsync(new NodeHealthyCheckFireEvent()
            {

            });
        }
    }
}
