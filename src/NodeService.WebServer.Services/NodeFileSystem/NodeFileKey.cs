namespace NodeService.WebServer.Services.NodeFileSystem;

public record struct NodeFileKey
{
    public NodeFileKey(string nodeInfoId, string fullName)
    {
        NodeInfoId = nodeInfoId;
        FullName = fullName;
    }

    public string NodeInfoId { get; private set; }

    public string FullName { get; private set; }
}
