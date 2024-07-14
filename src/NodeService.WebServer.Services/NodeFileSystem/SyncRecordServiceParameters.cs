using OneOf;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public record struct SyncRecordServiceParameters
    {
        public SyncRecordServiceParameters(AddOrUpdateSyncRecordParameters parameters)
        {
            Parameters = parameters;
        }

        public SyncRecordServiceParameters(QuerySyncRecordParameters parameters)
        {
            Parameters = parameters;
        }

        public OneOf<
            AddOrUpdateSyncRecordParameters,
            QuerySyncRecordParameters
            > Parameters
        { get; private set; }
    }
}
