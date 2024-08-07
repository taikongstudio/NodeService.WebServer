using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Extensions
{
    public static class TaskExecutionStatusExtensions
    {
        public static string GetDisplayName(this TaskExecutionStatus status)
        {
            return status switch
            {
                TaskExecutionStatus.Unknown => "未知",
                TaskExecutionStatus.Triggered => "已触发",
                TaskExecutionStatus.Pendding => "等待启动",
                TaskExecutionStatus.Started => "已启动",
                TaskExecutionStatus.Running => "运行中",
                TaskExecutionStatus.Failed => "失败",
                TaskExecutionStatus.Finished => "完成",
                TaskExecutionStatus.Cancelled => "已取消",
                TaskExecutionStatus.PenddingTimeout => "等待超时",
                TaskExecutionStatus.PenddingCancel => "等待取消",
                _ => "未知",
            };
        }
    }
}
