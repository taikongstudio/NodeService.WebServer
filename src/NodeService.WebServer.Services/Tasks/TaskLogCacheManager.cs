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
        public const string TaskKeyPrefix = "Task_";
        private readonly TaskLogDatabase _taskLogDatabase;
        private readonly ConcurrentDictionary<string, TaskLogCache> _taskLogCaches;
        private readonly ILogger<TaskLogCacheManager> _logger;
        private JsonSerializerOptions _jsonSerializerOptions;
        private long _initialized;
        public int PageSize { get; private set; }
        public TaskLogCacheManager(
            ILogger<TaskLogCacheManager> logger,
            TaskLogDatabase taskLogDatabase,
            int pageSize = 512)
        {
            _logger = logger;
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
        }

        public TaskLogCache GetCache(string taskId)
        {
            var taskLogCache = _taskLogCaches.GetOrAdd(taskId, EnsureTaskLogCache);
            return taskLogCache;
        }

        private TaskLogCache EnsureTaskLogCache(string taskId)
        {
            TaskLogCache? taskLogCache = null;
            if (!_taskLogDatabase.TryReadTask<TaskLogCacheDump>(
                TaskLogCache.BuildTaskKey(taskId),
                out var taskLogCacheDump) || taskLogCacheDump == null)
            {
                taskLogCache = new TaskLogCache(taskId, PageSize);
            }
            else
            {
                taskLogCache = new TaskLogCache(taskLogCacheDump);
            }
            taskLogCache.Database = _taskLogDatabase;
            return taskLogCache;
        }

        public void Load()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            _logger.LogInformation("Start read log record");
            int count = 0;
            try
            {
                var taskLogCacheDumps = _taskLogDatabase.ReadTasksWithPrefix<TaskLogCacheDump>(TaskKeyPrefix);
                foreach (var taskLogCacheDump in taskLogCacheDumps)
                {
                    TaskLogCache taskLogCache = new TaskLogCache(taskLogCacheDump);
                    taskLogCache.Database = _taskLogDatabase;
                    if (taskLogCache.CreationDateTimeUtc != DateTime.MinValue && DateTime.UtcNow - taskLogCache.CreationDateTimeUtc > TimeSpan.FromDays(7))
                    {
                        continue;
                    }
                    this._taskLogCaches.TryAdd(taskLogCacheDump.TaskId, taskLogCache);
                    count++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
            }
            finally
            {
                stopwatch.Stop();
                _logger.LogInformation($"finish read {count} log record,spent:{stopwatch.Elapsed}");
            }
        }

        public void Flush()
        {
            foreach (var taskLogCache in this._taskLogCaches.Values)
            {
                taskLogCache.Flush();
                _logger.LogInformation($"task log cache:{taskLogCache.TaskId} flush");
            }
            RemoveTaskLogCaches();
        }

        private void RemoveTaskLogCaches()
        {
            _logger.LogInformation($"begin remove task log cache");
            List<TaskLogCache> taskLogCacheList = new List<TaskLogCache>();
            foreach (var item in this._taskLogCaches)
            {
                if (DateTime.UtcNow - item.Value.LastWriteTimeUtc > TimeSpan.FromHours(6))
                {
                    taskLogCacheList.Add(item.Value);
                }
            }
            foreach (var taskLogCache in taskLogCacheList)
            {
                this._taskLogCaches.TryRemove(taskLogCache.TaskId, out _);
                _logger.LogInformation($"remove task log cache:{taskLogCache.TaskId}");
                _logger.LogInformation($"Remove {taskLogCache.TaskId},LastWriteTimeUtc:{taskLogCache.LastWriteTimeUtc}");
            }
            _logger.LogInformation($"remove task log caches:{taskLogCacheList.Count}");
        }
    }
}
