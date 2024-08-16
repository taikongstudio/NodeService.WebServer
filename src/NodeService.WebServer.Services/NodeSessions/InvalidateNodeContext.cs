namespace NodeService.WebServer.Services.NodeSessions
{
    public class InvalidateNodeContext
    {
        public InvalidateNodeContext(NodeInfoModel nodeInfo, bool removeCache)
        {
            NodeInfo = nodeInfo;
            RemoveCache = removeCache;
        }

        public NodeInfoModel NodeInfo { get; private set; }

        public bool RemoveCache { get; private set; }

    }
}
