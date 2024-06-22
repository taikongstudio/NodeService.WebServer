using NodeService.Infrastructure.NodeSessions;

namespace NodeService.WebServer.Services.NodeSessions;

public class NodeHealthyCounterDictionary : ConcurrentDictionary<NodeId, NodeHealthyCounter>
{
    public NodeHealthyCounter Ensure(NodeId nodeId)
    {
        return GetOrAdd(nodeId, new NodeHealthyCounter());
    }
}