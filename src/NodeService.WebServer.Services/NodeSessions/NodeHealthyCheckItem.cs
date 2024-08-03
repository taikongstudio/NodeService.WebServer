namespace NodeService.WebServer.Services.NodeSessions;

public class NodeHealthyCheckItem
{
    public string Solution { get; set; }

    public string Exception { get; set; }

}

public class NodeHeathyResult
{
    public NodeInfoModel NodeInfo { get; set; }

    public List<NodeHealthyCheckItem> Items { get; set; } = [];

}