using AntDesign;
using NodeService.Infrastructure.DataModels;

namespace NodeService.WebServer.UI.Helpers;

public static class NodeStatusHelper
{
    public static string GetBadgeStatus(this NodeStatus status)
    {
        switch (status)
        {
            case NodeStatus.Offline:
                return BadgeStatus.Warning;
            case NodeStatus.Online:
                return BadgeStatus.Success;
            default:
                return BadgeStatus.Processing;
        }
    }
}