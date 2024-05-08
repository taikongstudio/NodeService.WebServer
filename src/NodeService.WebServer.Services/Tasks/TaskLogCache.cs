using System.Text.Json.Serialization;
using NodeService.Infrastructure.Logging;

namespace NodeService.WebServer.Services.Tasks;

public class TaskLogCacheDump
{
    public TaskLogCacheDump()
    {
        PageDumps = new List<TaskLogCachePageDump>();
    }

    public string TaskId { get; set; }
    public int PageSize { get; set; }

    public int PageCount { get; set; }

    public bool IsTruncated { get; set; }

    public int Count { get; set; }

    public DateTime CreationDateTimeUtc { get; set; }

    public DateTime LastWriteTimeUtc { get; set; }

    public DateTime LastAccessTimeUtc { get; set; }

    public List<TaskLogCachePageDump> PageDumps { get; set; }
}

public class TaskLogCache
{
    readonly LinkedList<TaskLogCachePage> _pages;
    [JsonIgnore] long _count;

    public TaskLogCache(string taskId, int pageSize)
    {
        CreationDateTimeUtc = DateTime.UtcNow;
        TaskId = taskId;
        PageSize = pageSize;
        _pages = new LinkedList<TaskLogCachePage>();
        LoadedDateTime = DateTime.UtcNow;
    }

    public TaskLogCache(TaskLogCacheDump dump) : this(dump.TaskId, dump.PageSize)
    {
        PageCount = dump.PageCount;
        _count = dump.Count;
        CreationDateTimeUtc = dump.CreationDateTimeUtc;
        LastAccessTimeUtc = dump.LastAccessTimeUtc;
        LastAccessTimeUtc = dump.LastAccessTimeUtc;
        IsTruncated = dump.IsTruncated;
        foreach (var pageDump in dump.PageDumps)
            _pages.AddLast(new LinkedListNode<TaskLogCachePage>(new TaskLogCachePage(pageDump)));
    }

    public string TaskId { get; }

    public int PageSize { get; }

    public int Count => (int)Interlocked.Read(ref _count);

    public int PageCount { get; set; }

    public DateTime CreationDateTimeUtc { get; set; }

    public DateTime LastWriteTimeUtc { get; set; }

    public DateTime LastAccessTimeUtc { get; set; }

    public bool IsTruncated { get; set; }

    [JsonIgnore] public DateTime LoadedDateTime { get; set; }

    [JsonIgnore] public TaskLogDatabase Database { get; set; }

    public IEnumerable<LogEntry> GetEntries(int pageIndex, int pageSize)
    {
        if (IsTruncated) yield break;

        var startIndex = pageIndex * pageSize;
        var endIndex = Math.Min(Count, (pageIndex + 1) * pageSize);
        var startPageIndex = EntryIndexToPageIndex(startIndex, PageSize, out var startPageOffset);
        var endPageIndex = EntryIndexToPageIndex(endIndex, PageSize, out var endPageOffset);

        var currentPageIndex = startPageIndex;
        lock (_pages)
        {
            while (true)
            {
                var node = GetNode(currentPageIndex);
                if (node == null) yield break;
                foreach (var logEntry in GetPageEntries(ref node.ValueRef))
                    if (logEntry.Index >= startIndex && logEntry.Index < endIndex)
                        yield return logEntry;
                currentPageIndex++;
                if (currentPageIndex >= endPageIndex) break;
            }
        }
    }

    IEnumerable<LogEntry> GetPageEntries(ref TaskLogCachePage taskLogCachePage)
    {
        LastAccessTimeUtc = DateTime.UtcNow;
        if (taskLogCachePage.IsPersistenced)
            return Database.ReadLogEntries<LogEntry>(TaskId, taskLogCachePage.PageIndex, taskLogCachePage.PageSize);
        return taskLogCachePage.GetEntries();
    }


    public void AppendEntries(IEnumerable<LogEntry> logEntries)
    {
        LastWriteTimeUtc = DateTime.UtcNow;
        lock (_pages)
        {
            if (logEntries == null) return;
            foreach (var entryGroup in logEntries.Select(CheckLogEntry).GroupBy(CalculateLogEntryPageIndex))
            {
                var pageIndex = entryGroup.Key;
                var node = GetNode(pageIndex);
                if (node == null)
                {
                    node = _pages.AddLast(new TaskLogCachePage(TaskId, PageSize, pageIndex));
                    PageCount++;
                }

                node.ValueRef.AddRange(entryGroup);
                node.ValueRef.Verify();
            }
        }
    }

    LogEntry CheckLogEntry(LogEntry logEntry)
    {
        logEntry.Index = Count;
        Interlocked.Increment(ref _count);
        return logEntry;
    }

    LinkedListNode<TaskLogCachePage>? GetNode(int nodeIndex)
    {
        var current = _pages.First;
        for (var i = 0; i < nodeIndex; i++)
        {
            if (current == null) break;
            current = current.Next;
        }

        return current;
    }

    int CalculateLogEntryPageIndex(LogEntry logEntry)
    {
        var pageIndex = EntryIndexToPageIndex(logEntry.Index, PageSize, out _);
        return pageIndex;
    }

    int EntryIndexToPageIndex(int entryIndex, int pageSize, out int pageOffset)
    {
        return Math.DivRem(entryIndex, pageSize, out pageOffset);
    }

    public void Flush()
    {
        lock (_pages)
        {
            var current = _pages.First;
            while (current != null)
            {
                if (current.ValueRef.FlushedCount < current.ValueRef.PageSize)
                {
                    var oldCount = Database.GetEntriesCount(TaskId);
                    if (current.ValueRef.PageSize - current.ValueRef.FlushedCount == 0) continue;
                    var newCount = Database.AppendLogEntries(
                        TaskId, current.ValueRef.GetEntries(
                            current.ValueRef.FlushedCount, current.ValueRef.PageSize));
                    current.ValueRef.IncrementFlushedCount(newCount - oldCount);
                    if (current.ValueRef.FlushedCount == current.ValueRef.PageSize) current.ValueRef.Dispose();
                }

                current = current.Next;
            }
        }

        Database.WriteTask(BuildTaskKey(TaskId), CreateDump());
    }

    public static string BuildTaskKey(string taskId)
    {
        return $"{TaskLogCacheManager.TaskKeyPrefix}{taskId}";
    }

    TaskLogCacheDump CreateDump()
    {
        var taskLogCacheDump = new TaskLogCacheDump();
        Dump(taskLogCacheDump);
        return taskLogCacheDump;
    }

    public void Dump(TaskLogCacheDump taskLogCacheDump)
    {
        taskLogCacheDump.TaskId = TaskId;
        taskLogCacheDump.PageCount = PageCount;
        taskLogCacheDump.PageSize = PageSize;
        taskLogCacheDump.Count = Count;
        taskLogCacheDump.IsTruncated = IsTruncated;
        var current = _pages.First;
        while (current != null)
        {
            var pageDump = new TaskLogCachePageDump();
            current.ValueRef.Dump(pageDump);
            taskLogCacheDump.PageDumps.Add(pageDump);
            current = current.Next;
        }
    }

    public void Clear()
    {
        _pages.Clear();
    }

    public void Truncate()
    {
        lock (_pages)
        {
            foreach (var page in _pages) RemovePageEntries(page);
        }

        IsTruncated = true;
        Database.WriteTask(BuildTaskKey(TaskId), CreateDump());

        void RemovePageEntries(TaskLogCachePage page)
        {
            Database.ClearLogEntries(TaskId, page.PageIndex, page.PageSize);
        }
    }
}