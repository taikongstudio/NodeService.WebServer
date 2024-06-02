namespace NodeService.WebServer.Services.VirtualSystem;
using NodeService.Infrastructure.Models;
public class VirtualFileSystemInfoHelper
{


    public static VirtualFileSystemObjectInfo FromFtpListItem(FtpListItem ftpListItem)
    {
        return new VirtualFileSystemObjectInfo
        {
            CreationTime = ftpListItem.Created,
            FullName = ftpListItem.FullName,
            LastWriteTime = ftpListItem.Modified,
            Length = ftpListItem.Size,
            Name = ftpListItem.Name,
            Type = FromFtpObjectType(ftpListItem.Type)
        };
    }

    private static VirtualFileSystemObjectType FromFtpObjectType(FtpObjectType ftpObjectType)
    {
        return ftpObjectType switch
        {
            FtpObjectType.File => VirtualFileSystemObjectType.File,
            FtpObjectType.Directory => VirtualFileSystemObjectType.Directory,
            _ => VirtualFileSystemObjectType.NotSupported
        };
    }
}