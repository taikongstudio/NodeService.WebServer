﻿using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Services.Tasks;

public class TaskLogPersistenceService : BackgroundService
{
    private class SaveLogStat
    {
        public uint PageCreated;
        public uint LogEntriesWritten;
    }

    readonly ExceptionCounter _exceptionCounter;
    readonly BatchQueue<TaskLogGroup> _taskLogGroupBatchQueue;
    readonly WebServerCounter _webServerCounter;
    private readonly IMemoryCache _memoryCache;
    readonly ILogger<TaskLogPersistenceService> _logger;
    readonly ApplicationRepositoryFactory<TaskLogModel> _taskLogRepoFactory;

    public TaskLogPersistenceService(
        ILogger<TaskLogPersistenceService> logger,
        ApplicationRepositoryFactory<TaskLogModel> taskLogRepoFactory,
        BatchQueue<TaskLogGroup> taskLogGroupBatchQueue,
        ExceptionCounter exceptionCounter,
        WebServerCounter webServerCounter,
        IMemoryCache memoryCache
    )
    {
        _logger = logger;
        _taskLogRepoFactory = taskLogRepoFactory;
        _exceptionCounter = exceptionCounter;
        _taskLogGroupBatchQueue = taskLogGroupBatchQueue;
        _webServerCounter = webServerCounter;
        _memoryCache = memoryCache;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var arrayPoolCollection in _taskLogGroupBatchQueue.ReceiveAllAsync(stoppingToken))
        {
            try
            {
                using var taskLogRepo = _taskLogRepoFactory.CreateRepository();
                var stopwatch = new Stopwatch();
                var addedTaskLogPageList = new ConcurrentDictionary<string, TaskLogModel>();
                var updatedTaskLogPageList = new ConcurrentDictionary<string, TaskLogModel>();
                foreach (var taskLogGroup in arrayPoolCollection)
                {
                    stopwatch.Restart();
                    var saveLogStat = await SaveTaskLogAsync(
                        taskLogRepo,
                        taskLogGroup,
                        addedTaskLogPageList,
                        updatedTaskLogPageList,
                        stoppingToken);
                    stopwatch.Stop();
                    _webServerCounter.TaskExecutionReportSaveLogEntriesTimeSpan += stopwatch.Elapsed;
                    _webServerCounter.TaskExecutionReportLogEntriesCount += (uint)taskLogGroup.LogEntries.Length;
                    _webServerCounter.TaskExecutionReportLogEntriesPageCount += saveLogStat.PageCreated;
                    if (stopwatch.Elapsed > _webServerCounter.TaskExecutionReportSaveLogEntriesMaxTimeSpan)
                    {
                        _webServerCounter.TaskExecutionReportSaveLogEntriesMaxTimeSpan = stopwatch.Elapsed;
                    }
                    addedTaskLogPageList.Clear();
                    updatedTaskLogPageList.Clear();
                }
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
        }
    }

    private async Task<SaveLogStat> SaveTaskLogAsync(
        IRepository<TaskLogModel> taskLogRepo,
        TaskLogGroup taskLogGroup,
        ConcurrentDictionary<string, TaskLogModel> addedTaskLogPageDictionary,
        ConcurrentDictionary<string, TaskLogModel> updatedTaskLogPageDictionary,
        CancellationToken stoppingToken = default)
    {
        var saveLogStat = new SaveLogStat();
        int logCount = taskLogGroup.LogEntries.Length;
        int logIndex = 0;

        var infoKey = $"{nameof(TaskLogPersistenceService)}{taskLogGroup.Id}_0";
        if (!_memoryCache.TryGetValue<TaskLogModel>(infoKey, out var taskInfoLog) || taskInfoLog == null)
        {
            taskInfoLog = await taskLogRepo.FirstOrDefaultAsync(
            new TaskLogSpecification(taskLogGroup.Id, 0),
            stoppingToken);
            if (taskInfoLog == null)
            {
                taskInfoLog = new TaskLogModel()
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = taskLogGroup.Id,
                    ActualSize = logCount,
                    PageIndex = 0,
                    PageSize = 1
                };
                addedTaskLogPageDictionary.AddOrUpdate(taskInfoLog.Id, taskInfoLog, (key, oldValue) => taskInfoLog);

            }
            _memoryCache.Set(infoKey, taskInfoLog, TimeSpan.FromMinutes(10));
        }

        while (logIndex < logCount)
        {
            string key = CreateKey(taskLogGroup.Id, taskInfoLog.PageSize);
            if (!_memoryCache.TryGetValue<TaskLogModel>(key, out var taskLog))
            {
                taskLog = await taskLogRepo.FirstOrDefaultAsync(new TaskLogSpecification(taskLogGroup.Id, taskInfoLog.PageSize, 1), stoppingToken);
                if (taskLog != null && taskLog.PageIndex == taskInfoLog.PageSize)
                {
                    _memoryCache.Set(key, taskLog, TimeSpan.FromMinutes(1));
                }
                else
                {
                    taskLog = null;
                }
            }

            if (taskLog == null || taskLog.ActualSize == taskLog.PageSize)
            {
                _memoryCache.Remove(key);
                if (taskLog != null && taskLog.ActualSize == taskLog.PageSize)
                {
                    taskInfoLog.PageSize += 1;
                }
                taskLog = new TaskLogModel
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = taskLogGroup.Id,
                    PageIndex = taskInfoLog.PageSize,
                    PageSize = 512,
                };
                saveLogStat.PageCreated += 1;
                addedTaskLogPageDictionary.AddOrUpdate(taskLog.Id, taskLog, (key, oldValue) => taskLog);
                updatedTaskLogPageDictionary.AddOrUpdate(taskInfoLog.Id, taskInfoLog, (key, oldValue) => taskInfoLog);
                key = CreateKey(taskLogGroup.Id, taskLog.PageIndex);
                _memoryCache.Set(key, taskLog, TimeSpan.FromMinutes(1));
            }
            if (taskLog.ActualSize < taskLog.PageSize)
            {
                int takeCount = Math.Min(taskLog.PageSize - taskLog.ActualSize, taskLogGroup.LogEntries.Length);
                taskLog.LogEntries = taskLog.LogEntries.Union(taskLogGroup.LogEntries.Skip(logIndex).Take(takeCount)).ToList();
                taskLog.ActualSize = taskLog.LogEntries.Count;
                logIndex += takeCount;
                saveLogStat.LogEntriesWritten += (uint)takeCount;
                taskInfoLog.ActualSize += takeCount;
                if (!addedTaskLogPageDictionary.ContainsKey(taskInfoLog.Id))
                {
                    updatedTaskLogPageDictionary.AddOrUpdate(taskInfoLog.Id, taskInfoLog, (key, oldValue) => taskInfoLog);
                }
            }
            if (!addedTaskLogPageDictionary.ContainsKey(taskLog.Id))
            {
                updatedTaskLogPageDictionary.AddOrUpdate(taskLog.Id, taskLog, (key, oldValue) => taskLog);
            }
        }


        if (!addedTaskLogPageDictionary.IsEmpty)
        {
            await taskLogRepo.AddRangeAsync(addedTaskLogPageDictionary.Values, stoppingToken);
        }
        if (!updatedTaskLogPageDictionary.IsEmpty)
        {
            await taskLogRepo.UpdateRangeAsync(updatedTaskLogPageDictionary.Values, stoppingToken);
        }


        return saveLogStat;
    }

    static string CreateKey(string taskId, int pageSize)
    {
        return $"{nameof(TaskLogPersistenceService)}_{taskId}_{pageSize}";
    }
}