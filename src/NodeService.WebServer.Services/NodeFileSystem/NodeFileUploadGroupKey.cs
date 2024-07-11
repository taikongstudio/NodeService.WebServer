using NodeService.Infrastructure.NodeFileSystem;

namespace NodeService.WebServer.Services.NodeFileSystem;

public record struct NodeFileUploadGroupKey
{
    public string NodeInfoId { get; init; }
    public NodeFileSyncConfigurationProtocol ConfigurationProtocol { get; init; }
    public string ConfigurationId { get; init; }
}
