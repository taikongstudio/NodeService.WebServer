using OneOf;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public record struct NodeFileSystemInfoIndexServiceParameters
    {
        public NodeFileSystemInfoIndexServiceParameters(NodeFileSystemInfoQueryParameters parameters)
        {
            this.Parameters = parameters;
        }

        public OneOf<NodeFileSystemInfoQueryParameters> Parameters { get; init; }
    }
}
