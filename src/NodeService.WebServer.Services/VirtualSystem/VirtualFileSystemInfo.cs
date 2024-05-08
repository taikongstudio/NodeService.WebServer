namespace NodeService.WebServer.Services.VirtualSystem;

public class VirtualFileSystemInfo
{
    public string Name { get; set; }

    public string FullName { get; set; }

    public VirtualFileSystemObjectType Type { get; set; }

    public long Length { get; set; }

    public DateTime CreationTime { get; set; }

    public DateTime LastWriteTime { get; set; }

    public static VirtualFileSystemInfo FromFtpListItem(FtpListItem ftpListItem)
    {
        return new VirtualFileSystemInfo
        {
            CreationTime = ftpListItem.Created,
            FullName = ftpListItem.FullName,
            LastWriteTime = ftpListItem.Modified,
            Length = ftpListItem.Size,
            Name = ftpListItem.Name,
            Type = FromFtpObjectType(ftpListItem.Type)
        };
    }

    static VirtualFileSystemObjectType FromFtpObjectType(FtpObjectType ftpObjectType)
    {
        return ftpObjectType switch
        {
            FtpObjectType.File => VirtualFileSystemObjectType.File,
            FtpObjectType.Directory => VirtualFileSystemObjectType.Directory,
            _ => VirtualFileSystemObjectType.NotSupported
        };
    }
}