using NodeService.Infrastructure.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.Tasks
{
    public class TaskLogCacheManager
    {
        public const string Key = "Task";
        private readonly TaskLogDatabase _taskLogDatabase;
        private readonly ConcurrentDictionary<string, TaskLogCache> _taskLogCaches;
        private JsonSerializerOptions _jsonSerializerOptions;
        private long _initialized;
        public int PageSize {  get; private  set; }
        public TaskLogCacheManager(TaskLogDatabase taskLogDatabase, int pageSize = 512)
        {
            _jsonSerializerOptions = new JsonSerializerOptions()
            {
                IncludeFields = true,
            };
            _taskLogDatabase = taskLogDatabase;
            if (Debugger.IsAttached)
            {
                _taskLogDatabase.Reset();
            }
            PageSize = pageSize;
            _taskLogCaches = new ConcurrentDictionary<string, TaskLogCache>();
            ReadTaskLogCaches();
        }

        public TaskLogCache GetCache(string taskId)
        {
            var taskLogCache = _taskLogCaches.GetOrAdd(taskId, CreateTaskLogCache);
            return taskLogCache;
        }

        private TaskLogCache CreateTaskLogCache(string taskId)
        {
            var taskLogCache = new TaskLogCache(taskId, PageSize)
            {
                Database = _taskLogDatabase
            };
            return taskLogCache;
        }

        private void ReadTaskLogCaches()
        {
            try
            {
                var taskLogCacheDumps = _taskLogDatabase.ReadTasksWithPrefix<TaskLogCacheDump>(
                    Key
                    );
                foreach (var taskLogCacheDump in taskLogCacheDumps)
                {
                    TaskLogCache taskLogCache = new TaskLogCache(taskLogCacheDump);
                    taskLogCache.Database = _taskLogDatabase;
                    this._taskLogCaches.TryAdd(taskLogCacheDump.TaskId, taskLogCache);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

        }

        public void Flush()
        {
            foreach (var taskLogCache in this._taskLogCaches.Values)
            {
                taskLogCache.Flush();
            }
        }




    }
}
