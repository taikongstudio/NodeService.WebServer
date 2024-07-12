namespace NodeService.WebServer.Services.NodeFileSystem
{
    public record struct NodeFileSystemSyncRecordQueryParameters
    {
        public NodeFileSystemSyncRecordQueryParameters(QueryNodeFileSystemSyncRecordParameters parameters)
        {
            this.QueryParameters = parameters;
        }

        public QueryNodeFileSystemSyncRecordParameters QueryParameters { get; private set; }
    }
}
