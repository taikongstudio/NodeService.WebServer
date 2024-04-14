using NodeService.Infrastructure.Logging;
using System.Buffers;
using System.Drawing.Printing;

namespace NodeService.WebServer.Services.Tasks
{
    public class TaskLogCachePageDump
    {
        public string TaskId {  get; set; }
        public int PageSize { get; set; }

        public int PageIndex { get; set; }

        public int FlushedCount { get; set; }

        public int EntriesCount { get; set; }
    }


    public struct TaskLogCachePage : IDisposable
    {
        private LogEntry[] _entries;

        private long _entriesOffset;

        private long _flushedCount;

        public string TaskId { get; private set; }

        public int EntriesCount { get { return (int)Interlocked.Read(ref this._entriesOffset); } }

        public int PageIndex {  get; private set; }

        public int PageSize {  get; private set; }

        public int FlushedCount { get {return (int)Interlocked.Read(ref this._flushedCount); } }

        public void IncrementFlushedCount(int value)
        {
            var newValue = this.FlushedCount + value;
            if (newValue < 0 || newValue > this.PageSize)
            {
                return;
            }
            Interlocked.Exchange(ref this._flushedCount, newValue);
        }

        public bool IsPersistenced { get { return this.FlushedCount == this.EntriesCount; } }

        public bool IsDisposed { get; private set; }

        public TaskLogCachePage(string taskId, int pageSize, int pageIndex)
        {
            TaskId = taskId;
            PageSize = pageSize;
            this._entries = ArrayPool<LogEntry>.Shared.Rent(pageSize);
            this.PageIndex = pageIndex;
        }

        public TaskLogCachePage(TaskLogCachePageDump taskLogCachePageDump)
        {
            TaskId = taskLogCachePageDump.TaskId;
            PageSize = taskLogCachePageDump.PageSize;
            PageIndex = taskLogCachePageDump.PageIndex;
            _flushedCount = taskLogCachePageDump.FlushedCount < 0 ? 0 : taskLogCachePageDump.FlushedCount;
            _entriesOffset = taskLogCachePageDump.EntriesCount;
            if (_entriesOffset < PageSize)
            {
                this._entries = ArrayPool<LogEntry>.Shared.Rent(PageSize);
            }
            else
            {
                this.IsDisposed = true;
            }
        }

        public void Dispose()
        {
            CheckIsDisposed();
            if (this._entries != null)
            {
                ArrayPool<LogEntry>.Shared.Return(this._entries, true);
                this._entries = null;
            }
            this.IsDisposed = true;
        }

        public void AddEntry(LogEntry entry)
        {
            CheckIsDisposed();
            this._entries[this._entriesOffset] = entry;
            this._entriesOffset++;
        }

        private readonly void CheckIsDisposed()
        {
            if (this.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(this._entries));
            }
        }

        public void AddRange(IEnumerable<LogEntry> logEntries)
        {
            CheckIsDisposed();
            lock (this._entries)
            {
                foreach (var entry in logEntries)
                {
                    this.AddEntry(entry);
                }
            }
        }

        public IEnumerable<LogEntry> GetEntries()
        {
            CheckIsDisposed();
            return GetEntries(0, this.EntriesCount);
        }

        public IEnumerable<LogEntry> GetEntries(int offset, int count)
        {
            CheckIsDisposed();
            if (offset < 0 || offset > this.EntriesCount)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            int limit = Math.Min(count, this.EntriesCount);
            for (int i = offset; i < limit; i++)
            {
                yield return this._entries[i];
            }
            yield break;
        }

        public void Verify()
        {
            if (!Debugger.IsAttached)
            {
                return;
            }
            CheckIsDisposed();
            for (int i = this.FlushedCount; i < this.EntriesCount; i++)
            {
                var entry = this._entries[i];
                if (entry.Index % this.PageSize != i)
                {
                    throw new InvalidOperationException();
                }
            }
        }

        public void Dump(TaskLogCachePageDump taskLogCachePageDump)
        {
            taskLogCachePageDump.TaskId = this.TaskId;
            taskLogCachePageDump.EntriesCount = this.EntriesCount;
            taskLogCachePageDump.PageSize = this.PageSize;
            taskLogCachePageDump.PageIndex = this.PageIndex;
            taskLogCachePageDump.FlushedCount = this.FlushedCount;
        }


    }
}
