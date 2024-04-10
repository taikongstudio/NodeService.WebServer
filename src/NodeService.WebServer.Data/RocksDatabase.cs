using Microsoft.Extensions.Logging;
using RocksDbSharp;
using System.Text;

namespace NodeService.Infrastructure.Models
{
    public class RocksDatabase : IDisposable
    {
        readonly BlockBasedTableOptions _bbto;
        readonly DbOptions _options;
        readonly ColumnFamilies _columnFamilies;
        readonly FlushOptions _flushOptions;
        readonly ILogger<RocksDatabase> _logger;
        string logColumnName = "log";
        readonly RocksDbSharp.RocksDb _rocksDb;
        public string DatabasePath { get; private set; }



        public RocksDatabase(
            ILogger<RocksDatabase> logger
            )
        {
            _logger = logger;

            var logDbDirectory = Path.Combine(AppContext.BaseDirectory, "../logdb");
            this.DatabasePath = logDbDirectory;

            _bbto = new BlockBasedTableOptions()
            .SetFilterPolicy(BloomFilterPolicy.Create(10, false))
            .SetWholeKeyFiltering(false);

            _options = new DbOptions()
                .SetCreateIfMissing(true)
                .SetCreateMissingColumnFamilies(true);

            _columnFamilies = new ColumnFamilies
                {
                    { "default", new ColumnFamilyOptions().OptimizeForPointLookup(256) },
                    { "log", new ColumnFamilyOptions()
                        //.SetWriteBufferSize(writeBufferSize)
                        //.SetMaxWriteBufferNumber(maxWriteBufferNumber)
                        //.SetMinWriteBufferNumberToMerge(minWriteBufferNumberToMerge)
                        .SetMemtableHugePageSize(2 * 1024 * 1024)
                        .SetPrefixExtractor(SliceTransform.CreateFixedPrefix((ulong)41))
                        .SetBlockBasedTableFactory(_bbto)
                    },
                };
            _flushOptions = new FlushOptions();
            _flushOptions.SetWaitForFlush(true);

            _rocksDb = RocksDbSharp.RocksDb.Open(_options, this.DatabasePath, _columnFamilies);
        }



        public int WriteEntries<T>(string id, IEnumerable<T> entries, Func<int, T, bool>? func = null)
        {
            try
            {
                var cf = _rocksDb.GetColumnFamily(logColumnName);
                if (!int.TryParse(_rocksDb.Get(id), out var index))
                {
                    index = 0;
                }
                WriteBatch writeBatch = new WriteBatch();
                foreach (var entry in entries)
                {
                    var key = GetKey(id, index);
                    if (func != null && !func.Invoke(index, entry))
                    {
                        continue;
                    }
                    var value = JsonSerializer.Serialize(entry);
                    writeBatch.Put(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value), cf: cf);
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

        private string GetKey(string id, int index)
        {
            return $"{id}_Log_{index}";
        }

        public int GetEntriesCount(string id)
        {
            var cf = _rocksDb.GetColumnFamily(logColumnName);
            var value = _rocksDb.Get(id);
            if (!int.TryParse(value, out var index))
            {
                index = 0;
            }
            return index;
        }

        public IEnumerable<T> ReadEntries<T>(string id,
            int pageIndex,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var readOptions = new ReadOptions();
            var cf = _rocksDb.GetColumnFamily(logColumnName);
            var logLength = GetEntriesCount(id);
            for (int index = pageSize * pageIndex; index < logLength && index < (pageIndex + 1) * pageSize; index++)
            {
                var key = GetKey(id, index);
                var value = _rocksDb.Get(key, cf, encoding: Encoding.UTF8);
                yield return JsonSerializer.Deserialize<T>(value);
            }
            yield break;
        }

        public void Dispose()
        {
            _rocksDb.Dispose();
        }
    }
}
