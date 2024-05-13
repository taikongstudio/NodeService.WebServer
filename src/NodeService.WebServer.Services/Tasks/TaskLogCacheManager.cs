using NodeService.WebServer.Data;
using NodeService.WebServer.Models;

namespace NodeService.WebServer.Services.Tasks;

public class TaskLogCacheManager
{
    public const string TaskKeyPrefix = "Task_";
    readonly ILogger<TaskLogCacheManager> _logger;
    readonly ConcurrentDictionary<string, TaskLogCache> _taskLogCaches;
    readonly ExceptionCounter _exceptionCounter;
    readonly TaskLogDatabase _taskLogDatabase;
    long _initialized;
    JsonSerializerOptions _jsonSerializerOptions;

    public TaskLogCacheManager(
        ILogger<TaskLogCacheManager> logger,
        TaskLogDatabase taskLogDatabase,
        ExceptionCounter  exceptionCounter,
        int pageSize = 512)
    {
        _logger = logger;
        _jsonSerializerOptions = new JsonSerializerOptions
        {
            IncludeFields = true
        };
        _taskLogDatabase = taskLogDatabase;
        if (Debugger.IsAttached) _taskLogDatabase.Reset();
        PageSize = pageSize;
        _taskLogCaches = new ConcurrentDictionary<string, TaskLogCache>();
        _exceptionCounter = exceptionCounter;
    }

    public int PageSize { get; }

    public TaskLogCache GetCache(string taskId)
    {
        var taskLogCache = _taskLogCaches.GetOrAdd(taskId, EnsureTaskLogCache);
        return taskLogCache;
    }

    TaskLogCache EnsureTaskLogCache(string taskId)
    {
        TaskLogCache? taskLogCache = null;
        if (!_taskLogDatabase.TryReadTask<TaskLogCacheDump>(
                TaskLogCache.BuildTaskKey(taskId),
                out var taskLogCacheDump) || taskLogCacheDump == null)
            taskLogCache = new TaskLogCache(taskId, PageSize);
        else
            taskLogCache = new TaskLogCache(taskLogCacheDump);
        taskLogCache.Database = _taskLogDatabase;
        return taskLogCache;
    }

    public void Load()
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Start read log record");
        var count = 0;
        try
        {
            var taskLogCacheDumps = _taskLogDatabase.ReadTasksWithPrefix<TaskLogCacheDump>(TaskKeyPrefix);
            foreach (var taskLogCacheDump in taskLogCacheDumps)
            {
                var taskLogCache = new TaskLogCache(taskLogCacheDump);
                taskLogCache.Database = _taskLogDatabase;
                if (taskLogCache.IsTruncated) continue;
                TryTruncateCache(taskLogCache);
                if (taskLogCache.IsTruncated) continue;
                _taskLogCaches.TryAdd(taskLogCacheDump.TaskId, taskLogCache);
                count++;
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogInformation($"finish read {count} log record,spent:{stopwatch.Elapsed}");
        }
    }

    void TryTruncateCache(TaskLogCache taskLogCache)
    {
        if (taskLogCache.CreationDateTimeUtc.ToLocalTime() != DateTime.MinValue &&
            DateTime.UtcNow - taskLogCache.CreationDateTimeUtc > TimeSpan.FromDays(7))
        {
            taskLogCache.Truncate();
            _logger.LogInformation($"Truncate cache:{taskLogCache.TaskId}");
        }
    }

    public void Flush()
    {
        foreach (var taskLogCache in _taskLogCaches.Values)
        {
            taskLogCache.Flush();
            _logger.LogInformation($"task log cache:{taskLogCache.TaskId} flush");
        }

        DetachTaskLogCaches();
    }

    void DetachTaskLogCaches()
    {
        _logger.LogInformation("begin remove task log cache");
        var taskLogCacheList = new List<TaskLogCache>();
        foreach (var item in _taskLogCaches)
            if (DateTime.UtcNow - item.Value.LastWriteTimeUtc > TimeSpan.FromHours(6)
                &&
                DateTime.UtcNow - item.Value.LoadedDateTime > TimeSpan.FromMinutes(10)
               )
                taskLogCacheList.Add(item.Value);
        foreach (var taskLogCache in taskLogCacheList)
        {
            if (_taskLogCaches.TryRemove(taskLogCache.TaskId, out _))
            {
                TryTruncateCache(taskLogCache);
                taskLogCache.Clear();
                _logger.LogInformation($"remove task log cache:{taskLogCache.TaskId}");
                _logger.LogInformation($"Remove {taskLogCache.TaskId},LastWriteTimeUtc:{taskLogCache.LastWriteTimeUtc}");

            }
        }
        _logger.LogInformation($"remove task log caches:{taskLogCacheList.Count}");
    }
}