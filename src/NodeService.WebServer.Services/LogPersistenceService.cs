using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Logging;
using NodeService.Infrastructure.Models;
using NodeService.WebServer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services
{
    public class LogPersistenceService : BackgroundService
    {
        private readonly BatchQueue<LogPersistenceGroup> _logPersistenceBatchQueue;
        private readonly RocksDatabase _rocksDatabase;
        private readonly ILogger<LogPersistenceService> _logger;

        public LogPersistenceService(
            ILogger<LogPersistenceService> logger,
            BatchQueue<LogPersistenceGroup> logPersistenceBatchQueue,
            RocksDatabase rocksDatabase
            )
        {
            _logPersistenceBatchQueue = logPersistenceBatchQueue;
            _rocksDatabase = rocksDatabase;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var arrayPoolCollection in _logPersistenceBatchQueue.ReceiveAllAsync(stoppingToken))
            {
                int count = 0;
                Stopwatch stopwatch = new Stopwatch();
                try
                {
                    count = arrayPoolCollection.CountNotNull();
                    if (count == 0)
                    {
                        continue;
                    }

                    foreach (var logMessageGroups in arrayPoolCollection.Where(static x => x != null).GroupBy(static x => x.Id))
                    {
                        var id = logMessageGroups.Key;
                        stopwatch.Start();
                        int entriesCount = this._rocksDatabase.GetEntriesCount(id);
                        int writenCount = this._rocksDatabase.WriteEntries(
                            id,
                            logMessageGroups.SelectMany(static x => x.LogMessageEntries),
                            FilterLogMessageEntry);
                        stopwatch.Stop();
                        _logger.LogInformation($"{id}:Write {writenCount - entriesCount} log message entries,spent:{stopwatch.Elapsed}");
                        stopwatch.Reset();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.ToString());
                }
                finally
                {
                    arrayPoolCollection.Dispose();
                }

            }
        }

        static private bool FilterLogMessageEntry(int index, LogMessageEntry entry)
        {
            entry.Index = index;
            return true;
        }

    }
}
