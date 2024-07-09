using NodeService.Infrastructure.DataModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Extensions
{
    public record struct TaskLogPageKey
    {
        public TaskLogPageKey(string taskExecutionInstanceId, int pageIndex)
        {
            TaskExecutionInstanceId = taskExecutionInstanceId;
            PageIndex = pageIndex;
        }

        public string TaskExecutionInstanceId { get; private set; }
        public int PageIndex { get; private set; }
    }

    public static class TaskLogModelExtensions
    {
        public static bool IsTaskLogPageChanged(this TaskLogModel taskLog)
        {
            if (taskLog == null)
            {
                return false;
            }
            if (taskLog.DirtyCount == 0) return false;
            if (taskLog.PageIndex == 0) return true;
            return
                        taskLog.ActualSize >= taskLog.PageSize
                        ||
                        taskLog.DirtyCount > 0;
        }

        public static bool IsFullTaskLogPage(this TaskLogModel taskLogPage)
        {
            return taskLogPage.PageIndex > 0 && taskLogPage.ActualSize >= taskLogPage.PageSize;
        }

        public static bool IsInactiveTaskLogPage(this TaskLogModel taskLog)
        {
            bool isInactive = false;
            if (isInactive)
            {
                return true;
            }
            return DateTime.UtcNow - taskLog.LastWriteTime > TimeSpan.FromMinutes(5);
        }

        public static TaskLogPageKey GetKey(this TaskLogModel taskLogPage)
        {
            if (taskLogPage == null)
            {
                return default;
            }
            TaskLogPageKey taskLogPageKey = default;
            if (taskLogPage.PageIndex == 0)
            {
                taskLogPageKey = CreateTaskLogPageKey(taskLogPage.Id, taskLogPage.PageIndex);

            }
            else
            {
                taskLogPageKey = CreateTaskLogPageKey(taskLogPage.TaskExecutionInstanceId, taskLogPage.PageIndex);
            }
            return taskLogPageKey;
        }

        public static TaskLogPageKey CreateTaskLogInfoPageKey(string taskExecutionInstanceId)
        {
            return new TaskLogPageKey(taskExecutionInstanceId, 0);
        }

        public static TaskLogPageKey CreateTaskLogPageKey(string taskExecutionInstanceId, int index)
        {
            return new TaskLogPageKey(taskExecutionInstanceId, index);
        }

    }
}
