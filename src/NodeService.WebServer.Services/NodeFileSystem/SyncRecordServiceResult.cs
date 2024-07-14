using OneOf;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public record struct SyncRecordServiceResult
    {
        public SyncRecordServiceResult(SyncRecordServiceAddOrUpdateResult result)
        {
            Result = result;
        }

        public SyncRecordServiceResult(SyncRecordServiceQueryResult result)
        {
            Result = result;
        }

        public OneOf<SyncRecordServiceAddOrUpdateResult, SyncRecordServiceQueryResult> Result { get; private set; }
    }
}
