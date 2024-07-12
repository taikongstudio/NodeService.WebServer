namespace NodeService.WebServer.Services.NodeFileSystem
{
    public record struct NodeFileSystemSyncRecordServiceAddOrUpdateResult
    {
        public int AddedCount { get; init; }

        public int ModifiedCount { get; init; }
    }
}
