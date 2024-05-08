using System.Buffers;
using NodeService.Infrastructure.Logging;

namespace NodeService.WebServer.Services.Tasks;

public class TaskLogCachePageDump
{
    public string TaskId { get; set; }
    public int PageSize { get; set; }

    public int PageIndex { get; set; }

    public int FlushedCount { get; set; }

    public int EntriesCount { get; set; }
}

public struct TaskLogCachePage : IDisposable
{
    LogEntry[]? _entries;

    long _entriesOffset;

    long _flushedCount;

    public string TaskId { get; }

    public int EntriesCount => (int)Interlocked.Read(ref _entriesOffset);

    public int PageIndex { get; }

    public int PageSize { get; }

    public int FlushedCount => (int)Interlocked.Read(ref _flushedCount);

    public void IncrementFlushedCount(int value)
    {
        var newValue = FlushedCount + value;
        if (newValue < 0 || newValue > PageSize) return;
        Interlocked.Exchange(ref _flushedCount, newValue);
    }

    public bool IsPersistenced => FlushedCount == EntriesCount;

    public bool IsDisposed { get; set; }

    public TaskLogCachePage(string taskId, int pageSize, int pageIndex)
    {
        TaskId = taskId;
        PageSize = pageSize;
        _entries = ArrayPool<LogEntry>.Shared.Rent(pageSize);
        PageIndex = pageIndex;
    }

    public TaskLogCachePage(TaskLogCachePageDump taskLogCachePageDump)
    {
        TaskId = taskLogCachePageDump.TaskId;
        PageSize = taskLogCachePageDump.PageSize;
        PageIndex = taskLogCachePageDump.PageIndex;
        _flushedCount = taskLogCachePageDump.FlushedCount < 0 ? 0 : taskLogCachePageDump.FlushedCount;
        _entriesOffset = taskLogCachePageDump.EntriesCount;
        if (_entriesOffset < PageSize)
            _entries = ArrayPool<LogEntry>.Shared.Rent(PageSize);
        else
            IsDisposed = true;
    }

    public void Dispose()
    {
        CheckIsDisposed();
        if (_entries != null)
        {
            ArrayPool<LogEntry>.Shared.Return(_entries, true);
            _entries = null;
        }

        IsDisposed = true;
    }

    public void AddEntry(LogEntry entry)
    {
        CheckIsDisposed();
        _entries[_entriesOffset] = entry;
        _entriesOffset++;
    }

    readonly void CheckIsDisposed()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(_entries));
    }

    public void AddRange(IEnumerable<LogEntry> logEntries)
    {
        CheckIsDisposed();
        lock (_entries)
        {
            foreach (var entry in logEntries) AddEntry(entry);
        }
    }

    public IEnumerable<LogEntry> GetEntries()
    {
        CheckIsDisposed();
        return GetEntries(0, EntriesCount);
    }

    public IEnumerable<LogEntry> GetEntries(int offset, int count)
    {
        CheckIsDisposed();
        if (offset < 0 || offset > EntriesCount) throw new ArgumentOutOfRangeException(nameof(offset));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var limit = Math.Min(count, EntriesCount);
        for (var i = offset; i < limit; i++) yield return _entries[i];
    }

    public void Verify()
    {
        if (!Debugger.IsAttached) return;
        CheckIsDisposed();
        for (var i = FlushedCount; i < EntriesCount; i++)
        {
            var entry = _entries[i];
            if (entry.Index % PageSize != i) throw new InvalidOperationException();
        }
    }

    public void Dump(TaskLogCachePageDump taskLogCachePageDump)
    {
        taskLogCachePageDump.TaskId = TaskId;
        taskLogCachePageDump.EntriesCount = EntriesCount;
        taskLogCachePageDump.PageSize = PageSize;
        taskLogCachePageDump.PageIndex = PageIndex;
        taskLogCachePageDump.FlushedCount = FlushedCount;
    }
}