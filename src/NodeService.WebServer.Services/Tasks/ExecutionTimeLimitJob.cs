using Microsoft.Extensions.DependencyInjection;
using NodeService.WebServer.Data;
using Quartz;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.Tasks
{
    public class ExecutionTimeLimitJob : JobBase
    {

        public override async Task Execute(IJobExecutionContext context)
        {
            var dbContextFactory = ServiceProvider.GetService<IDbContextFactory<ApplicationDbContext>>();
            using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var nodeSessionService = ServiceProvider.GetService<INodeSessionService>();
            var jobExecutionInstance = Properties["JobExecutionInstance"] as JobExecutionInstanceModel;
            var nodeId = new NodeId(jobExecutionInstance.NodeInfoId);
            foreach (var nodeSessionId in nodeSessionService.EnumNodeSessions(nodeId))
            {
                await nodeSessionService.SendJobExecutionEventAsync(nodeSessionId, jobExecutionInstance.ToCancelEvent());
            }
            if (AsyncDispoable != null)
            {
                await AsyncDispoable.DisposeAsync();
            }
        }
    }
}
