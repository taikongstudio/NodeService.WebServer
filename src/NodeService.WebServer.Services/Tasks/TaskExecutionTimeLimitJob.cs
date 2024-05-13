using Microsoft.Extensions.DependencyInjection;
using NodeService.Infrastructure.NodeSessions;

namespace NodeService.WebServer.Services.Tasks;

public class TaskExecutionTimeLimitJob : JobBase
{
    public override async Task Execute(IJobExecutionContext context)
    {
        var nodeSessionService = ServiceProvider.GetService<INodeSessionService>();
        var taskExecutionInstance = Properties["TaskExecutionInstance"] as JobExecutionInstanceModel;
        var nodeId = new NodeId(taskExecutionInstance.NodeInfoId);
        foreach (var nodeSessionId in nodeSessionService.EnumNodeSessions(nodeId))
            await nodeSessionService.SendJobExecutionEventAsync(nodeSessionId, taskExecutionInstance.ToCancelEvent());
        if (AsyncDispoable != null) await AsyncDispoable.DisposeAsync();
    }
}