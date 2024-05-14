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
        ExceptionCounter exceptionCounter,
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
        TaskLogCache taskLogCache = null;
        if (_taskLogDatabase.TryReadTask<TaskLogCacheDump>(
                TaskLogCache.BuildTaskKey(taskId),
                out var taskLogCacheDump) && taskLogCacheDump != null)
        {
            taskLogCache = new TaskLogCache(taskLogCacheDump);
        }
        else if (taskLogCacheDump == null)
        {
            taskLogCache = new TaskLogCache(taskId, PageSize);
        }
        if (taskLogCache != null)
        {
            taskLogCache.Database = _taskLogDatabase;
        }
        return taskLogCache;
    }


    void TryTruncateCache(TaskLogCache taskLogCache)
    {
        if (taskLogCache.CreationDateTimeUtc != DateTime.MinValue &&
            DateTime.UtcNow - taskLogCache.CreationDateTimeUtc > TimeSpan.FromDays(30))
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
        List<TaskLogCache> taskLogCacheList = new List<TaskLogCache>();
        foreach (var item in _taskLogCaches)
            if (ShouldDetach(item.Value))
            {
                taskLogCacheList.Add(item.Value);
            }

        foreach (var taskLogCache in taskLogCacheList)
        {
            if (_taskLogCaches.TryRemove(taskLogCache.TaskId, out _))
            {
                TryTruncateCache(taskLogCache);
                taskLogCache.Clear();
                _logger.LogInformation($"remove task log cache:{taskLogCache.TaskId}");
                _logger.LogInformation($"Remove {taskLogCache.TaskId},{nameof(TaskLogCache.LastAccessTimeUtc)}:{taskLogCache.LastAccessTimeUtc},{nameof(TaskLogCache.CreationDateTimeUtc)}:{taskLogCache.CreationDateTimeUtc},{nameof(TaskLogCache.LastWriteTimeUtc)}:{taskLogCache.LastWriteTimeUtc}");

            }
        }
        _logger.LogInformation($"remove task log caches:{taskLogCacheList.Count}");
    }

    private bool ShouldDetach(TaskLogCache item)
    {
        return DateTime.UtcNow - item.LastWriteTimeUtc > TimeSpan.FromHours(6)
                        &&
                        DateTime.UtcNow - item.LoadedDateTime > TimeSpan.FromMinutes(30);
    }
}