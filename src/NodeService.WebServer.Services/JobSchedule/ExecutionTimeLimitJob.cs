using Microsoft.Extensions.DependencyInjection;
using NodeService.WebServer.Data;
using Quartz;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.JobSchedule
{
    public class ExecutionTimeLimitJob : JobBase
    {

        public override async Task Execute(IJobExecutionContext context)
        {
            var dbContextFactory = ServiceProvider.GetService<IDbContextFactory<ApplicationDbContext>>();
            using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var nodeSessionService = ServiceProvider.GetService<INodeSessionService>();
            var jobExecutionInstance = this.Properties["JobExecutionInstance"] as JobExecutionInstanceModel;
            var nodeId = new NodeId(jobExecutionInstance.NodeInfoId);
            foreach (var nodeSessionId in nodeSessionService.EnumNodeSessions(nodeId))
            {
                await nodeSessionService.SendJobExecutionEventAsync(nodeSessionId, jobExecutionInstance.ToCancelEvent());
            }
            if (this.AsyncDispoable != null)
            {
                await this.AsyncDispoable.DisposeAsync();
            }
        }
    }
}
