using System.Text;
using Microsoft.Extensions.Logging;
using RocksDbSharp;

namespace NodeService.WebServer.Data;

public class TaskLogDatabase : IDisposable
{
    public const string LogColumn = "log";
    public const string TaskColumn = "task";

    readonly BlockBasedTableOptions _bbto;
    readonly ColumnFamilies _columnFamilies;
    readonly FlushOptions _flushOptions;
    readonly ILogger<TaskLogDatabase> _logger;
    readonly DbOptions _options;
    readonly RocksDb _rocksDb;

    public TaskLogDatabase(
        ILogger<TaskLogDatabase> logger
    )
    {
        _logger = logger;

        var logDbDirectory = Path.Combine(AppContext.BaseDirectory, "../tasklogdb");
        DatabasePath = logDbDirectory;

        _bbto = new BlockBasedTableOptions()
            .SetFilterPolicy(BloomFilterPolicy.Create(10, false))
            .SetWholeKeyFiltering(false);

        _options = new DbOptions()
            .SetCreateIfMissing()
            .SetCreateMissingColumnFamilies();

        _columnFamilies = new ColumnFamilies
        {
            { "default", new ColumnFamilyOptions().OptimizeForPointLookup(256) },
            {
                LogColumn, new ColumnFamilyOptions()
                    //.SetWriteBufferSize(writeBufferSize)
                    //.SetMaxWriteBufferNumber(maxWriteBufferNumber)
                    //.SetMinWriteBufferNumberToMerge(minWriteBufferNumberToMerge)
                    .SetMemtableHugePageSize(2 * 1024 * 1024)
                    .SetPrefixExtractor(SliceTransform.CreateFixedPrefix(41))
                    .SetBlockBasedTableFactory(_bbto)
            },
            {
                TaskColumn, new ColumnFamilyOptions()
                    //.SetWriteBufferSize(writeBufferSize)
                    //.SetMaxWriteBufferNumber(maxWriteBufferNumber)
                    //.SetMinWriteBufferNumberToMerge(minWriteBufferNumberToMerge)
                    .SetMemtableHugePageSize(2 * 1024 * 1024)
                    .SetPrefixExtractor(SliceTransform.CreateFixedPrefix(41))
                    .SetBlockBasedTableFactory(_bbto)
            }
        };
        _flushOptions = new FlushOptions();
        _flushOptions.SetWaitForFlush(true);
        _rocksDb = RocksDb.Open(_options, DatabasePath, _columnFamilies);
    }

    public string DatabasePath { get; }

    public void Dispose()
    {
        _rocksDb.Dispose();
    }

    public int WriteTask<T>(
        string id,
        T entry,
        Func<int, T, bool>? func = null,
        JsonSerializerOptions? options = null)
    {
        try
        {
            var cf = _rocksDb.GetColumnFamily(TaskColumn);
            if (func != null && !func.Invoke(0, entry)) return 0;

            var value = JsonSerializer.Serialize(entry, options);
            _rocksDb.Put(Encoding.UTF8.GetBytes(id), Encoding.UTF8.GetBytes(value), cf);
            _rocksDb.Flush(_flushOptions);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
        }

        return 0;
    }

    public int AppendLogEntries<T>(
        string id,
        IEnumerable<T> entries,
        Func<int, T, bool>? func = null,
        JsonSerializerOptions? options = null)
    {
        try
        {
            var cf = _rocksDb.GetColumnFamily(LogColumn);
            if (!int.TryParse(_rocksDb.Get(id), out var index)) index = 0;
            using var writeBatch = new WriteBatch();
            foreach (var entry in entries)
            {
                var key = GetKey(id, index);
                if (func != null && !func.Invoke(index, entry)) continue;

                var value = JsonSerializer.Serialize(entry, options);
                writeBatch.Put(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value), cf);
                index++;
            }

            _rocksDb.Write(writeBatch);
            _rocksDb.Put(id, index.ToString());
            _rocksDb.Flush(_flushOptions);
            return index;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
        }

        return 0;
    }

    string GetKey(string id, int index)
    {
        return $"{id}_Log_{index}";
    }

    public int GetEntriesCount(string key)
    {
        var value = _rocksDb.Get(key);
        if (!int.TryParse(value, out var index)) index = 0;
        return index;
    }

    public void WriteEntriesCount(string id, int count)
    {
        _rocksDb.Put(id, count.ToString());
    }

    public IEnumerable<T> ReadLogEntries<T>(
        string id,
        int pageIndex,
        int pageSize,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var cf = _rocksDb.GetColumnFamily(LogColumn);
        var logLength = GetEntriesCount(id);
        for (var index = pageSize * pageIndex; index < logLength && index < (pageIndex + 1) * pageSize; index++)
        {
            var key = GetKey(id, index);
            var value = _rocksDb.Get(key, cf, encoding: Encoding.UTF8);
            yield return JsonSerializer.Deserialize<T>(value, options);
        }
    }

    public void ClearLogEntries(string id, int pageIndex, int pageSize)
    {
        var cf = _rocksDb.GetColumnFamily(LogColumn);
        var logLength = GetEntriesCount(id);
        for (var index = pageSize * pageIndex; index < logLength && index < (pageIndex + 1) * pageSize; index++)
        {
            var key = GetKey(id, index);
            _rocksDb.Remove(key, cf);
        }

        _rocksDb.Flush(_flushOptions);
    }

    public IEnumerable<T> ReadTasksWithPrefix<T>(
        string prefix,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var cf = _rocksDb.GetColumnFamily(TaskColumn);
        var iter = _rocksDb.NewIterator(cf);
        iter.Seek(prefix);
        while (iter.Valid())
        {
            var value = iter.StringValue();
            yield return JsonSerializer.Deserialize<T>(value, options);
            iter.Next();
        }

        iter.Dispose();
    }

    public bool TryReadTask<T>(
        string key,
        out T? task,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        task = default;
        try
        {
            var cf = _rocksDb.GetColumnFamily(TaskColumn);
            var value = _rocksDb.Get(key, cf, encoding: Encoding.UTF8);
            if (value != null) task = JsonSerializer.Deserialize<T>(value, options);
        }
        catch (Exception ex)
        {
        }

        return task != null;
    }

    public void Reset()
    {
    }
}