using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using NodeService.Infrastructure.Logging;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;
using System.Data.Common;

namespace NodeService.WebServer.Services.Tasks
{
    public class MongodbLogStorageHandler : TaskLogStorageHandlerBase
    {
        private class TaskInfoCache
        {
            public ObjectId TaskLogId { get; set; }
            public TaskLogModel TaskInfoLog { get; set; }
        }

        private readonly ILogger<MongodbLogStorageHandler> _logger;
        private readonly ExceptionCounter _exceptionCounter;
        private readonly MongoDbOptions _mongoDbOptions;
        private readonly MongoGridFS _mongoGridFS;
        private readonly MongoClient _mongoClient;
        private readonly ApplicationRepositoryFactory<TaskLogModel> _taskLogRepoFactory;
        private readonly IMemoryCache _memoryCache;
        private readonly WebServerCounter _webServerCounter;

        public MongodbLogStorageHandler(
            ILogger<MongodbLogStorageHandler> logger,
            ExceptionCounter exceptionCounter,
            MongoGridFS  mongoGridFS,
            WebServerCounter webServerCounter,
            ApplicationRepositoryFactory<TaskLogModel> taskLogRepoFactory,
            IOptionsMonitor<MongoDbOptions> mongoDbOptions,
            IMemoryCache memoryCache)
        {
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _mongoDbOptions = mongoDbOptions.CurrentValue;
            _mongoGridFS = mongoGridFS;
            _taskLogRepoFactory = taskLogRepoFactory;
            _memoryCache = memoryCache;
            _webServerCounter = webServerCounter;
        }

        public override async ValueTask ProcessAsync(
            IEnumerable<TaskLogUnit> taskLogUnits,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (!taskLogUnits.Any())
                {
                    return;
                }
                if (Debugger.IsAttached)
                {
                    foreach (var taskLogUnitGroup in taskLogUnits.GroupBy(static x => x.Id))
                    {
                        await ProcessTaskLogUnitGroupAsync(taskLogUnitGroup, cancellationToken);

                    }
                }
                else
                {
                    await Parallel.ForEachAsync(taskLogUnits.GroupBy(static x => x.Id), new ParallelOptions()
                    {
                        CancellationToken = cancellationToken,
                        MaxDegreeOfParallelism = 2,
                    }, ProcessTaskLogUnitGroupAsync);
                }

            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());

            }
        }

        private async ValueTask ProcessTaskLogUnitGroupAsync(
            IGrouping<string, TaskLogUnit> taskLogUnitGroup,
            CancellationToken cancellationToken = default)
        {
            var taskExecutionInstanceId = taskLogUnitGroup.Key;
            if (taskExecutionInstanceId == null)
            {
                return;
            }
            _logger.LogInformation($"Process {taskExecutionInstanceId}");

            try
            {
                var logEntries = taskLogUnitGroup.SelectMany(static x => x.LogEntries).OrderBy(static x => x.DateTimeUtc);
                var stopwatch = Stopwatch.StartNew();
                var taskInfoCache = await EnsureTaskInfoCacheAsync(taskExecutionInstanceId, cancellationToken);
                stopwatch.Stop();

                _webServerCounter.Snapshot.TaskLogInfoQueryTimeSpan.Value += stopwatch.Elapsed;
                _webServerCounter.Snapshot.TaskLogEntriesSaveTimes.Value++;

                stopwatch.Restart();
                var entriesCount = await SaveLogEntriesAsync(taskInfoCache, logEntries, cancellationToken);
                stopwatch.Stop();
                if (stopwatch.Elapsed > _webServerCounter.Snapshot.TaskLogUnitSaveMaxTimeSpan.Value)
                {
                    _webServerCounter.Snapshot.TaskLogUnitSaveMaxTimeSpan.Value = stopwatch.Elapsed;
                }
                _webServerCounter.Snapshot.TaskLogUnitSaveTimeSpan.Value += stopwatch.Elapsed;
                stopwatch.Restart();
                await UpdateTaskLogInfoPageAsync(taskInfoCache.TaskInfoLog, cancellationToken);
                stopwatch.Stop();

                this._webServerCounter.Snapshot.TaskLogInfoSaveTimeSpan.Value += stopwatch.Elapsed;

                _logger.LogInformation($"Process {taskExecutionInstanceId},spent:{stopwatch.Elapsed},Save {entriesCount} logs");

                this._webServerCounter.Snapshot.TaskLogUnitConsumeCount.Value += taskLogUnitGroup.Count();
                this._webServerCounter.Snapshot.TaskLogEntriesSaveCount.Value += entriesCount;

            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());

            }
        }

        async ValueTask<int> SaveLogEntriesAsync(
            TaskInfoCache  taskInfoCache,
            IEnumerable<LogEntry> logEntries,
            CancellationToken cancellationToken = default)
        {
            StreamWriter streamWriter = null;
            var count = 0;
            try
            {

                var delete = false;
                if (delete)
                {
                    _mongoGridFS.DeleteById(taskInfoCache.TaskLogId);
                }
                if (!_mongoGridFS.ExistsById(taskInfoCache.TaskLogId))
                {
                    streamWriter = new StreamWriter(_mongoGridFS.Create(taskInfoCache.TaskLogId.ToString(), new MongoGridFSCreateOptions()
                    {
                        Id = taskInfoCache.TaskLogId,
                        UploadDate = DateTime.UtcNow,
                    }));
                }
                else
                {
                    streamWriter = _mongoGridFS.AppendText(taskInfoCache.TaskLogId.ToString());
                }
                var currentLogEntry = taskInfoCache.TaskInfoLog.LogEntries.LastOrDefault();
                if (currentLogEntry == null)
                {
                    currentLogEntry = new LogEntry();
                    taskInfoCache.TaskInfoLog.LogEntries.Add(currentLogEntry);
                }
                streamWriter.BaseStream.Position = currentLogEntry.Type;

                foreach (var item in logEntries)
                {
                    await streamWriter.WriteLineAsync(JsonSerializer.Serialize(item));
                    taskInfoCache.TaskInfoLog.ActualSize += 1;
                    currentLogEntry.Status++;
                    count++;
                    if (currentLogEntry.Status == 1024)
                    {
                        await streamWriter.FlushAsync(cancellationToken);
                        currentLogEntry.Type = streamWriter.BaseStream.Position;
                        currentLogEntry = new LogEntry()
                        {
                            Index = currentLogEntry.Index + 1,
                        };
                        taskInfoCache.TaskInfoLog.LogEntries = [.. taskInfoCache.TaskInfoLog.LogEntries, currentLogEntry];

                        _webServerCounter.Snapshot.TaskLogPageCount.Value++;
                    }
                }

                await streamWriter.FlushAsync(cancellationToken);
                currentLogEntry.Type = streamWriter.BaseStream.Position;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                _exceptionCounter.AddOrUpdate(ex);
            }
            finally
            {
                if (streamWriter != null)
                {
                    await streamWriter.DisposeAsync();
                }
            }

            return count;
        }




        //private GridFSBucket EnsureBucket()
        //{

        //    var database = _mongoClient.GetDatabase(_mongoDbOptions.TaskLogDatabaseName);
        //    var options = new GridFSBucketOptions
        //    {
        //        BucketName = "TaskLogFileBucket",
        //        ChunkSizeBytes = 255 * 1024,//255 MB is the default value,
        //        WriteConcern = WriteConcern.W1,
        //        ReadPreference = ReadPreference.Secondary
        //    };
        //    var bucket = new GridFSBucket(database, options);
        //    return bucket;
        //}

        async ValueTask<TaskInfoCache> EnsureTaskInfoCacheAsync(
               string taskExecutionInstanceId,
               CancellationToken cancellationToken = default)
        {
            TaskInfoCache? taskInfoCache = default;
            var taskLogInfoKey = $"TaskLogInfoCacheKey_{TaskLogModelExtensions.CreateTaskLogInfoPageKey(taskExecutionInstanceId)}";
            if (!_memoryCache.TryGetValue<TaskInfoCache>(taskLogInfoKey, out taskInfoCache) || taskInfoCache == default)
            {
                taskInfoCache = new TaskInfoCache();
                await using var taskLogRepo = await _taskLogRepoFactory.CreateRepositoryAsync(cancellationToken);
                var taskLogInfoPage = await taskLogRepo.GetByIdAsync(taskExecutionInstanceId, cancellationToken);
                if (taskLogInfoPage != null)
                {
                    taskInfoCache.TaskInfoLog = taskLogInfoPage;
                    string? id = null;
                    if (taskLogInfoPage.Name != null)
                    {
                        id = taskLogInfoPage.Name?["mongodb://".Length..];
                    }
                    if (id != null)
                    {
                        taskInfoCache.TaskLogId = ObjectId.Parse(id);
                        _memoryCache.Set(taskLogInfoKey, taskInfoCache, TimeSpan.FromDays(14));
                    }
                }
            }
            if (taskInfoCache.TaskLogId == default)
            {
                await using var taskLogRepo = await _taskLogRepoFactory.CreateRepositoryAsync(cancellationToken);
                taskInfoCache.TaskLogId = ObjectId.GenerateNewId();
                taskInfoCache.TaskInfoLog = CreateTaskLogInfoPage(taskExecutionInstanceId, taskInfoCache.TaskLogId);
                await taskLogRepo.AddAsync(taskInfoCache.TaskInfoLog, cancellationToken);
                _memoryCache.Set(taskLogInfoKey, taskInfoCache, TimeSpan.FromDays(14));
            }

            return taskInfoCache;
        }

        public async ValueTask UpdateTaskLogInfoPageAsync(TaskLogModel taskLog, CancellationToken cancellationToken = default)
        {
        LRetry:
            try
            {
                await using var taskLogRepo = await _taskLogRepoFactory.CreateRepositoryAsync(cancellationToken);
                await taskLogRepo.UpdateAsync(taskLog, cancellationToken);
            }
            catch (DbException ex)
            {
                _logger.LogError(ex.ToString());
                _exceptionCounter.AddOrUpdate(ex);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                goto LRetry;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                _exceptionCounter.AddOrUpdate(ex);
            }

        }

        static TaskLogModel CreateTaskLogInfoPage(string taskExecutionInstanceId,ObjectId objectId)
        {
            return new TaskLogModel()
            {
                Id = taskExecutionInstanceId,
                Name = $"mongodb://{objectId}",
                ActualSize = 0,
                PageIndex = 0,
                PageSize = 1
            };
        }
    }
}
