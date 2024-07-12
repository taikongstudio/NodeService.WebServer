using NodeService.Infrastructure.NodeFileSystem;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public class NodeFileSystemWatchEvent
    {
        public NodeFileSystemWatchEvent()
        {

        }

        public required string NodeFilePath { get; init; }

        public required string NodeFilePathHash { get; init; }

        public NodeFileInfo? ObjectInfo { get; init; }
    }
}
