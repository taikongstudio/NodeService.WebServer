using NodeService.Infrastructure.Data;
using OneOf;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public record struct NodeFileSystemInfoIndexServiceResult
    {
        public NodeFileSystemInfoIndexServiceResult(ListQueryResult<NodeFileSystemInfoModel> result)
        {
            Result = result;
        }

        public OneOf<ListQueryResult<NodeFileSystemInfoModel>> Result { get; init; }
    }
}
