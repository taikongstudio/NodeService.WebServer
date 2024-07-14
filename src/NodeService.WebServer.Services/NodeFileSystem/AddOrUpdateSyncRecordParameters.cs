using System.Collections.Immutable;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public record struct AddOrUpdateSyncRecordParameters
    {
        public AddOrUpdateSyncRecordParameters(NodeFileSyncRecordModel syncRecord)
        {
            SyncRecords = [syncRecord];
        }

        public AddOrUpdateSyncRecordParameters(IEnumerable<NodeFileSyncRecordModel> syncRecords)
        {
            SyncRecords = [.. syncRecords];
        }

        public AddOrUpdateSyncRecordParameters(ImmutableArray<NodeFileSyncRecordModel> syncRecords)
        {
            SyncRecords = syncRecords;
        }

        public IEnumerable<NodeFileSyncRecordModel> SyncRecords { get; private set; }
    }
}
