using OneOf;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public record struct NodeFileSystemSyncRecordServiceParameters
    {
        public NodeFileSystemSyncRecordServiceParameters(NodeFileSystemAddOrUpdateSyncRecordParameters parameters)
        {
            Parameters = parameters;
        }

        public NodeFileSystemSyncRecordServiceParameters(NodeFileSystemSyncRecordQueryParameters parameters)
        {
            Parameters = parameters;
        }

        public OneOf<
            NodeFileSystemAddOrUpdateSyncRecordParameters,
            NodeFileSystemSyncRecordQueryParameters
            > Parameters
        { get; private set; }
    }
}
