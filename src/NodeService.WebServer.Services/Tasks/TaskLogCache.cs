using MimeKit.Encodings;
using NodeService.Infrastructure.Logging;
using NodeService.Infrastructure.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.Tasks
{
    public class TaskLogCacheDump
    {
        public TaskLogCacheDump()
        {
            this.PageDumps = new List<TaskLogCachePageDump>();
        }

        public string TaskId { get; set; }
        public int PageSize { get; set; }

        public int PageCount { get; set; }

        public int Count { get; set; }

        public DateTime DateTimeUtc { get; set; }

        public List<TaskLogCachePageDump> PageDumps { get; set; }
    }

    public class TaskLogCache
    {
        private LinkedList<TaskLogCachePage> _pages;

        [JsonIgnore]
        private long _count;

        public string TaskId { get; private set; }

        public int PageSize { get; private set; }

        public int Count { get { return (int)Interlocked.Read(ref this._count); } }

        public int PageCount { get; private set; }

        public DateTime DateTimeUtc { get; private set; }

        [JsonIgnore]
        public TaskLogDatabase Database { get; set; }

        public TaskLogCache(string taskId, int pageSize)
        {
            TaskId = taskId;
            PageSize = pageSize;
            this._pages = new LinkedList<TaskLogCachePage>();
        }

        public TaskLogCache(TaskLogCacheDump dump) : this(dump.TaskId, dump.PageSize)
        {
            this.PageCount = dump.PageCount;
            this._count = dump.Count;
            foreach (var pageDump in dump.PageDumps)
            {
                this._pages.AddLast(new LinkedListNode<TaskLogCachePage>(new TaskLogCachePage(pageDump)));
            }
        }

        public IEnumerable<LogEntry> GetEntries(int pageIndex, int pageSize)
        {
            int startIndex = pageIndex * pageSize;
            int endIndex = Math.Min(this.Count, (pageIndex + 1) * pageSize);
            int startPageIndex = EntryIndexToPageIndex(startIndex, PageSize, out var startPageOffset);
            int endPageIndex = EntryIndexToPageIndex(endIndex, PageSize, out var endPageOffset);

            var currentPageIndex = startPageIndex;
            lock (this._pages)
            {
                while (true)
                {
                    var node = GetNode(currentPageIndex);
                    if (node == null) { yield break; }
                    foreach (var logEntry in GetPageEntries(ref node.ValueRef))
                    {
                        if (logEntry.Index >= startIndex && logEntry.Index < endIndex)
                        {
                            yield return logEntry;

                        }
                    }
                    currentPageIndex++;
                    if (currentPageIndex >= endPageIndex)
                    {
                        break;
                    }
                }
            }

            yield break;
        }

        private IEnumerable<LogEntry> GetPageEntries(ref TaskLogCachePage taskLogCachePage)
        {
            if (taskLogCachePage.IsPersistenced)
            {
                return this.Database.ReadLogEntries<LogEntry>(this.TaskId, taskLogCachePage.PageIndex, taskLogCachePage.PageSize);
            }
            return taskLogCachePage.GetEntries();
        }

        public void AppendEntries(IEnumerable<LogEntry> logEntries)
        {
            lock (this._pages)
            {
                if (logEntries == null)
                {
                    return;
                }
                foreach (var entryGroup in logEntries.Select(CheckLogEntry).GroupBy(CalculateLogEntryPageIndex))
                {
                    var pageIndex = entryGroup.Key;
                    var node = GetNode(pageIndex);
                    if (node == null)
                    {
                        node = this._pages.AddLast(new TaskLogCachePage(TaskId, PageSize, pageIndex));
                        this.PageCount++;
                    }
                    node.ValueRef.AddRange(entryGroup);
                    node.ValueRef.Verify();
                }
            }

        }

        private LogEntry CheckLogEntry(LogEntry logEntry)
        {
            logEntry.Index = this.Count;
            Interlocked.Increment(ref this._count);
            return logEntry;
        }

        private LinkedListNode<TaskLogCachePage>? GetNode(int nodeIndex)
        {
            var current = this._pages.First;
            for (int i = 0; i < nodeIndex; i++)
            {
                if (current == null)
                {
                    break;
                }
                current = current.Next;
            }
            return current;
        }

        private int CalculateLogEntryPageIndex(LogEntry logEntry)
        {
            int pageIndex = EntryIndexToPageIndex(logEntry.Index, PageSize, out _);
            return pageIndex;
        }

        private int EntryIndexToPageIndex(int entryIndex, int pageSize, out int pageOffset)
        {
            return Math.DivRem(entryIndex, pageSize, out pageOffset);
        }

        public void Flush()
        {
            lock (this._pages)
            {
                var current = this._pages.First;
                while (current != null)
                {
                    if (current.ValueRef.FlushedCount < current.ValueRef.PageSize)
                    {
                        int oldCount = Database.GetEntriesCount(this.TaskId);
                        if (current.ValueRef.PageSize - current.ValueRef.FlushedCount == 0)
                        {
                            continue;
                        }
                        int newCount = Database.AppendLogEntries(
                            this.TaskId, current.ValueRef.GetEntries(
                                current.ValueRef.FlushedCount, current.ValueRef.PageSize));
                        current.ValueRef.IncrementFlushedCount(newCount - oldCount);
                        if (current.ValueRef.FlushedCount == current.ValueRef.PageSize)
                        {
                            current.ValueRef.Dispose();
                        }
                    }

                    current = current.Next;
                }
            }
            Database.WriteTask($"{TaskLogCacheManager.Key}_{TaskId}", this.CreateDump());
        }

        private TaskLogCacheDump CreateDump()
        {
            TaskLogCacheDump taskLogCacheDump = new TaskLogCacheDump();
            this.Dump(taskLogCacheDump);
            return taskLogCacheDump;
        }

        public void Dump(TaskLogCacheDump taskLogCacheDump)
        {
            taskLogCacheDump.TaskId = this.TaskId;
            taskLogCacheDump.PageCount = this.PageCount;
            taskLogCacheDump.PageSize = this.PageSize;
            taskLogCacheDump.Count = this.Count;
            var current = this._pages.First;
            while (current != null)
            {
                TaskLogCachePageDump pageDump = new TaskLogCachePageDump();
                current.ValueRef.Dump(pageDump);
                taskLogCacheDump.PageDumps.Add(pageDump);
                current = current.Next;
            }
        }
    }
}
