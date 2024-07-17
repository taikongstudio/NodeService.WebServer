using MongoDB.Driver.GridFS;
using MongoDB.Driver;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NodeService.WebServer.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using System.IO;
using NodeService.Infrastructure.Logging;
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

        public MongodbLogStorageHandler(
            ILogger<MongodbLogStorageHandler> logger,
            ExceptionCounter exceptionCounter,
            MongoGridFS  mongoGridFS,
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
                foreach (var taskLogUnitGroup in taskLogUnits.GroupBy(static x => x.Id))
                {
                    var taskId = taskLogUnitGroup.Key;
                    if (taskId == null)
                    {
                        continue;
                    }
                    _logger.LogInformation($"Process {taskId}");
                    var logEntries = taskLogUnitGroup.SelectMany(x => x.LogEntries).OrderBy(x => x.DateTimeUtc);
                    try
                    {
                        Stopwatch stopwatch = Stopwatch.StartNew();
                        var taskInfoCache = await EnsureTaskInfoCacheAsync(taskId, cancellationToken);
                        bool delete = false;
                        if (delete)
                        {
                            _mongoGridFS.DeleteById(taskInfoCache.TaskLogId);
                        }

                        StreamWriter streamWriter = null;
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
                        var lastLogEntry = taskInfoCache.TaskInfoLog.LogEntries.LastOrDefault();
                        if (lastLogEntry == null)
                        {
                            lastLogEntry = new LogEntry();
                            taskInfoCache.TaskInfoLog.LogEntries.Add(lastLogEntry);
                        }
                        else if (lastLogEntry.Status == 1024)
                        {
                            lastLogEntry = new LogEntry()
                            {
                                Index = lastLogEntry.Index + 1,
                                Type = lastLogEntry.Type,
                            };
                            taskInfoCache.TaskInfoLog.LogEntries = [.. taskInfoCache.TaskInfoLog.LogEntries, lastLogEntry];
                        }
                        streamWriter.BaseStream.Position = lastLogEntry.Type;
                        int count = 0;
                        foreach (var item in logEntries)
                        {
                            await streamWriter.WriteLineAsync(JsonSerializer.Serialize(item));
                            taskInfoCache.TaskInfoLog.ActualSize += 1;
                            lastLogEntry.Status++;
                            if (lastLogEntry.Status == 1024)
                            {
                                await streamWriter.FlushAsync(cancellationToken);
                                lastLogEntry.Type = streamWriter.BaseStream.Position;
                                lastLogEntry = new LogEntry()
                                {
                                    Index = lastLogEntry.Index + 1,
                                };
                                taskInfoCache.TaskInfoLog.LogEntries = [.. taskInfoCache.TaskInfoLog.LogEntries, lastLogEntry];
                            }
                            count++;
                        }

                        await streamWriter.FlushAsync(cancellationToken);
                        lastLogEntry.Type = streamWriter.BaseStream.Position;
                        await streamWriter.DisposeAsync();
                        await UpdateTaskLogInfoPageAsync(taskInfoCache.TaskInfoLog, cancellationToken);
                        stopwatch.Stop();
                        _logger.LogInformation($"Process {taskId},spent:{stopwatch.Elapsed},Save {count} logs");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex.ToString());
                        _exceptionCounter.AddOrUpdate(ex);
                    }

                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                _exceptionCounter.AddOrUpdate(ex);
            }


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
                await using var taskLogRepo = await _taskLogRepoFactory.CreateRepositoryAsync();
                var taskLogInfoPage = await taskLogRepo.GetByIdAsync(taskExecutionInstanceId, cancellationToken);
                var id = taskLogInfoPage?.Name?["mongodb://".Length..];
                taskInfoCache.TaskInfoLog = taskLogInfoPage;
                if (id != null)
                {
                    taskInfoCache.TaskLogId = ObjectId.Parse(id);
                    _memoryCache.Set(taskLogInfoKey, taskInfoCache, TimeSpan.FromDays(14));
                }

            }
            if (taskInfoCache.TaskLogId == default)
            {
                await using var taskLogRepo = await _taskLogRepoFactory.CreateRepositoryAsync();
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
