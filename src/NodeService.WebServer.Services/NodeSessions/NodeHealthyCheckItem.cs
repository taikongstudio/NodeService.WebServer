namespace NodeService.WebServer.Services.NodeSessions;

public class NodeHealthyCheckItem
{
    public string Solution { get; set; }

    public string Exception { get; set; }

}

public class NodeHeathyResult
{
    public NodeInfoModel NodeInfo { get; set; }

    public string LastUpdateTime { get; set; }

    public string TestArea { get; set; }

    public string LabArea { get; set; }

    public string LabName { get; set; }

    public string Manager { get; set; }

    public string IpAddress { get; set; }

    public List<NodeHealthyCheckItem> Items { get; set; } = [];

}