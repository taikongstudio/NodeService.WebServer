using System.IO.Compression;

namespace NodeService.WebServer.Extensions;

public static class ZipArchiveHelper
{
    public const string PackageKey = "package.key";

    public static bool TryUpdate(Stream stream, out ZipArchive zipArchive)
    {
        zipArchive = null;
        try
        {
            zipArchive = new ZipArchive(stream, ZipArchiveMode.Update);
            return true;
        }
        catch (Exception ex)
        {
        }

        return false;
    }

    public static bool TryRead(Stream stream, out ZipArchive zipArchive)
    {
        zipArchive = null;
        try
        {
            zipArchive = new ZipArchive(stream, ZipArchiveMode.Read);
            return true;
        }
        catch (Exception ex)
        {
        }

        return false;
    }


    public static bool HasPackageKey(this ZipArchiveEntry zipArchiveEntry)
    {
        if (zipArchiveEntry == null) return false;
        if (zipArchiveEntry.Name != PackageKey) return false;
        var stream = zipArchiveEntry.Open();
        using var streamReader = new StreamReader(stream, leaveOpen: true);
        return streamReader.ReadLine() == "AD138DD2-D2E4-42BA-893F-6166C4AC5A9C";
    }
}