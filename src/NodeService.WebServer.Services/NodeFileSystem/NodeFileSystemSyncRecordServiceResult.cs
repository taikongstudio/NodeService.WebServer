using OneOf;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public record struct NodeFileSystemSyncRecordServiceResult
    {
        public NodeFileSystemSyncRecordServiceResult(NodeFileSystemSyncRecordServiceAddOrUpdateResult result)
        {
            Result = result;
        }

        public NodeFileSystemSyncRecordServiceResult(NodeFileSystemSyncRecordServiceQueryResult result)
        {
            Result = result;
        }

        public OneOf<NodeFileSystemSyncRecordServiceAddOrUpdateResult, NodeFileSystemSyncRecordServiceQueryResult> Result { get; private set; }
    }
}
