using NodeService.Infrastructure.DataModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Extensions
{
    public static class TaskFloweExecutionStatusExtensions
    {
        public static string GetDisplayName(this TaskFlowExecutionStatus status)
        {
            return status switch
            {
                TaskFlowExecutionStatus.Running => "运行中",
                TaskFlowExecutionStatus.PartialFinished => "部分完成",
                TaskFlowExecutionStatus.Finished => "完成",
                TaskFlowExecutionStatus.Fault => "错误",
                _ => "未知",
            };
        }
    }
}
