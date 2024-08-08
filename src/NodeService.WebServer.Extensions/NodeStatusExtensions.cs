using NodeService.Infrastructure.DataModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Extensions
{
    public static class NodeStatusExtensions
    {
        public static string GetDisplayName(this NodeStatus nodeStatus)
        {
            return nodeStatus switch
            {
                NodeStatus.NotConfigured => "未配置",
                NodeStatus.Offline => "离线",
                NodeStatus.Online => "在线",
                NodeStatus.All => "全部状态",
                _ => "未知",
            };
        }

    }
}
