using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Logging;
using NodeService.Infrastructure.Models;
using NodeService.WebServer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.NodeSessions
{
    public class LogPersistenceService : BackgroundService
    {
        private readonly BatchQueue<IEnumerable<LogPersistenceGroup>> _logPersistenceGroupsBatchQueue;
        private readonly RocksDatabase _rocksDatabase;
        private readonly ILogger<LogPersistenceService> _logger;

        public LogPersistenceService(
            ILogger<LogPersistenceService> logger,
            BatchQueue<IEnumerable<LogPersistenceGroup>> logPersistenceGroupsBatchQueue,
            RocksDatabase rocksDatabase
            )
        {
            _logPersistenceGroupsBatchQueue = logPersistenceGroupsBatchQueue;
            _rocksDatabase = rocksDatabase;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var arrayPoolCollection in _logPersistenceGroupsBatchQueue.ReceiveAllAsync(stoppingToken))
            {
                int count = arrayPoolCollection.CountNotNull();
                if (count == 0)
                {
                    continue;
                }
                Stopwatch stopwatch = new Stopwatch();
                int groupsCount = 0;
                int logEntiresCount = 0;
                try
                {
                    stopwatch.Start();
                    foreach (var logMessageGroups in arrayPoolCollection.Where(static x => x != null))
                    {
                        foreach (var logPersistenceGroup in logMessageGroups.GroupBy(static x => x.Id))
                        {
                            logEntiresCount += WriteLogEntries(logPersistenceGroup.Key, logPersistenceGroup.SelectMany(static x => x.EntriesList.SelectMany(static x => x)));
                            groupsCount++;
                        }
                    }
                    stopwatch.Stop();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.ToString());
                }
                finally
                {
                    arrayPoolCollection.Dispose();
                    _logger.LogInformation($"Write {groupsCount} groups,{logEntiresCount} logEntires,spent:{stopwatch.Elapsed},avg:{stopwatch.Elapsed.TotalMilliseconds / logEntiresCount}ms");
                }

            }
        }

        private int WriteLogEntries(string id, IEnumerable<LogEntry> logEntries)
        {
            Stopwatch stopwatch = new Stopwatch();
            int count = 0;
            stopwatch.Start();
            int totalCountOld = _rocksDatabase.GetEntriesCount(id);
            int totalCountNew = _rocksDatabase.WriteEntries(
                id,
                logEntries,
                FilterLogMessageEntry);
            stopwatch.Stop();
            count = totalCountNew - totalCountOld;
            _logger.LogInformation($"{id}:Write {count} log message entries,spent:{stopwatch.Elapsed}");
            stopwatch.Reset();
            return count;
        }

        static private bool FilterLogMessageEntry(int index, LogEntry entry)
        {
            entry.Index = index;
            return true;
        }

    }
}
