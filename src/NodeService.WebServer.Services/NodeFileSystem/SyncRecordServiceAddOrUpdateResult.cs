namespace NodeService.WebServer.Services.NodeFileSystem
{
    public record struct SyncRecordServiceAddOrUpdateResult
    {
        public int AddedCount { get; init; }

        public int ModifiedCount { get; init; }
    }
}
