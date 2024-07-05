namespace NodeService.WebServer.Services.VirtualFileSystem;

using NodeService.Infrastructure.NodeFileSystem;

public class VirtualFileSystemInfoHelper
{
    public static NodeFileInfo FromFtpListItem(FtpListItem ftpListItem)
    {
        return new NodeFileInfo
        {
            CreationTime = ftpListItem.Created,
            FullName = ftpListItem.FullName,
            LastWriteTime = ftpListItem.Modified,
            Length = ftpListItem.Size,
            Name = ftpListItem.Name,
            Attributes = ftpListItem.Type == FtpObjectType.Directory ? NodeFileAttributes.Directory : NodeFileAttributes.None
        };
    }
}