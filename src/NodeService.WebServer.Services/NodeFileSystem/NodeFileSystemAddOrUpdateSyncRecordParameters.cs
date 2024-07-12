using System.Collections.Immutable;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public record struct NodeFileSystemAddOrUpdateSyncRecordParameters
    {
        public NodeFileSystemAddOrUpdateSyncRecordParameters(NodeFileSyncRecordModel syncRecord)
        {
            SyncRecords = [syncRecord];
        }

        public NodeFileSystemAddOrUpdateSyncRecordParameters(IEnumerable<NodeFileSyncRecordModel> syncRecords)
        {
            SyncRecords = [.. syncRecords];
        }

        public NodeFileSystemAddOrUpdateSyncRecordParameters(ImmutableArray<NodeFileSyncRecordModel> syncRecords)
        {
            SyncRecords = syncRecords;
        }

        public IEnumerable<NodeFileSyncRecordModel> SyncRecords { get; private set; }
    }
}
