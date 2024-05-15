namespace NodeService.WebServer.Models;

public class WebServerOptions
{
    public int HeartBeatPeriod { get; set; } = 30000;

    public int HeartBearRandomMin { get; set; } = 30000;

    public int HearBeatRandomMax { get; set; } = 60000;

    public string Channel { get; set; }
    public string VirtualFileSystem { get; set; }
    public string FileCachesPathFormat { get; set; }
    public string PackagePathFormat { get; set; }
    public string JobLogPathFormat { get; set; }

    public bool DebugProductionMode { get; set; }


    public string GetPackagePath(string packageId)
    {
        return PackagePathFormat
            .Replace("{channel}", Channel)
            .Replace("{packageId}", packageId);
    }

    public string GetFileCachesPath(string nodeId)
    {
        return FileCachesPathFormat
            .Replace("{channel}", Channel)
            .Replace("{nodeId}", nodeId);
    }
}