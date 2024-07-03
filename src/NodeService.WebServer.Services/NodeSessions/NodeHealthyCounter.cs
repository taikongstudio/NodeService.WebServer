namespace NodeService.WebServer.Services.NodeSessions;

public class NodeHealthyCounter
{
    public DateTime LastSentNotificationDateTimeUtc { get; set; }
    public long SentNotificationCount { get; set; }

    public long OfflineCount { get; set; }
}