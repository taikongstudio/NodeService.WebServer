using OneOf;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public record struct NodeFileSystemInfoQueryParameters
    {
        public NodeFileSystemInfoQueryParameters(QueryNodeFileSystemInfoParameters value)
        {
            Value = value;
        }

        public OneOf<QueryNodeFileSystemInfoParameters> Value { get; init; }
    }
}
