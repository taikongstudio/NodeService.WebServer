namespace NodeService.WebServer.Services.NodeFileSystem
{
    public record struct QuerySyncRecordParameters
    {
        public QuerySyncRecordParameters(QueryNodeFileSystemSyncRecordParameters parameters)
        {
            this.QueryParameters = parameters;
        }

        public QueryNodeFileSystemSyncRecordParameters QueryParameters { get; private set; }
    }
}
