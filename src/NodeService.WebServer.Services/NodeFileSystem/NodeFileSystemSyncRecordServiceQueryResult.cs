using NodeService.Infrastructure.Data;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public record struct NodeFileSystemSyncRecordServiceQueryResult
    {
        public NodeFileSystemSyncRecordServiceQueryResult(ListQueryResult<NodeFileSyncRecordModel> result)
        {
            Result = result;
        }

        public ListQueryResult<NodeFileSyncRecordModel> Result { get; private set; }
    }
}
