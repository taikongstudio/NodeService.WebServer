using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Logging;
using NodeService.Infrastructure.Models;
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
        WebServerCounter webServerCounter
    )
    {
        _logger = logger;
        _taskLogRepoFactory = taskLogRepoFactory;
        _exceptionCounter = exceptionCounter;
        _taskLogGroupBatchQueue = taskLogGroupBatchQueue;
        _webServerCounter = webServerCounter;
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
                foreach (var taskLogGroups in arrayPoolCollection.GroupBy(x => x.Id))
                {
                    stopwatch.Restart();
                    var taskId = taskLogGroups.Key;
                    var groupsCount = taskLogGroups.Count();
                    var logEntries = taskLogGroups.SelectMany(static x => x.LogEntries);
                    var saveLogStat = await SaveTaskLogAsync(
                                 taskLogRepo,
                                 taskId,
                                 logEntries,
                                 addedTaskLogPageList,
                                 updatedTaskLogPageList,
                                 stoppingToken);

                    stopwatch.Stop();
                    _webServerCounter.TaskExecutionReportSaveLogEntriesTimeSpan += stopwatch.Elapsed;
                    _webServerCounter.TaskExecutionReportLogEntriesCount += (uint)groupsCount;
                    _webServerCounter.TaskExecutionReportLogEntriesPageCount += saveLogStat.PageCreated;
                    _webServerCounter.TaskExecutionReportLogGroupConsumeCount++;
                    if (stopwatch.Elapsed > _webServerCounter.TaskExecutionReportSaveLogEntriesMaxTimeSpan)
                    {
                        _webServerCounter.TaskExecutionReportSaveLogEntriesMaxTimeSpan = stopwatch.Elapsed;
                    }
                    addedTaskLogPageList.Clear();
                    updatedTaskLogPageList.Clear();
                }
                _webServerCounter.TaskExecutionReportLogGroupAvailableCount = (uint)_taskLogGroupBatchQueue.AvailableCount;
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
        }
    }

    async Task<SaveLogStat> SaveTaskLogAsync(
        IRepository<TaskLogModel> taskLogRepo,
        string taskId,
        IEnumerable<LogEntry> logEntries,
        ConcurrentDictionary<string, TaskLogModel> addedTaskLogPageDictionary,
        ConcurrentDictionary<string, TaskLogModel> updatedTaskLogPageDictionary,
        CancellationToken stoppingToken = default)
    {
        var saveLogStat = new SaveLogStat();
        int logCount = logEntries.Count();
        int logIndex = 0;

        var infoKey = $"{nameof(TaskLogPersistenceService)}{taskId}_0";
        var taskInfoLog = await taskLogRepo.FirstOrDefaultAsync(new TaskLogSpecification(taskId, 0), stoppingToken);
        if (taskInfoLog == null)
        {
            taskInfoLog = new TaskLogModel()
            {
                Id = Guid.NewGuid().ToString(),
                Name = taskId,
                ActualSize = logCount,
                PageIndex = 0,
                PageSize = 1
            };
            addedTaskLogPageDictionary.AddOrUpdate(taskInfoLog.Id, taskInfoLog, (key, oldValue) => taskInfoLog);
        }

        while (logIndex < logCount)
        {
           var taskLog = await taskLogRepo.FirstOrDefaultAsync(new TaskLogSpecification(taskId, taskInfoLog.PageSize, 1), stoppingToken);


            if (taskLog == null || taskLog.ActualSize == taskLog.PageSize)
            {
                if (taskLog != null && taskLog.ActualSize == taskLog.PageSize)
                {
                    taskInfoLog.PageSize += 1;
                }
                taskLog = new TaskLogModel
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = taskId,
                    PageIndex = taskInfoLog.PageSize,
                    PageSize = 256,
                };
                saveLogStat.PageCreated += 1;
                addedTaskLogPageDictionary.AddOrUpdate(taskLog.Id, taskLog, (key, oldValue) => taskLog);
                updatedTaskLogPageDictionary.AddOrUpdate(taskInfoLog.Id, taskInfoLog, (key, oldValue) => taskInfoLog);
            }
            if (taskLog.ActualSize < taskLog.PageSize)
            {
                int takeCount = Math.Min(taskLog.PageSize - taskLog.ActualSize, logCount);
                taskLog.LogEntries = taskLog.LogEntries.Union(logEntries.Skip(logIndex).Take(takeCount)).ToList();
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
}