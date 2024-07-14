using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.Dianostics
{
    public class GCService : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GCSettings.LatencyMode = GCLatencyMode.Batch;
                var isServerGC = GCSettings.IsServerGC;
                GC.Collect();
                await Task.Delay(TimeSpan.FromSeconds(60));
            }
        }
    }
}
