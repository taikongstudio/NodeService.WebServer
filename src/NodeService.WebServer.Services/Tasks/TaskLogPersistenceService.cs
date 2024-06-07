using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Logging;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using System.Linq;

namespace NodeService.WebServer.Services.Tasks;

public class TaskLogPersistenceService : BackgroundService
{
    private class SaveTaskLogStat
    {
        public ulong PageCreated;
        public ulong PageSaved;
        public ulong PageNotSaved;
        public ulong LogEntriesSaved;
    }

    private class LogPageCounterInfo
    {
        public LogPageCounterInfo(TaskLogModel log)
        {
            Log = log;
        }

        public int ChangesCount { get; set; }

        public bool IsFull { get { return Log.ActualSize == Log.PageSize; } }

        public DateTime Modified { get; set; }

        public TaskLogModel Log { get; private set; }

    }

    readonly ExceptionCounter _exceptionCounter;
    readonly BatchQueue<TaskLogGroup> _taskLogGroupBatchQueue;
    readonly WebServerCounter _webServerCounter;
    readonly ConcurrentDictionary<string, TaskLogModel> _addedTaskLogPageDictionary;
    readonly ConcurrentDictionary<string, TaskLogModel> _updatedTaskLogPageDictionary;
    readonly IMemoryCache _memoryCache;
    readonly ILogger<TaskLogPersistenceService> _logger;
    readonly ApplicationRepositoryFactory<TaskLogModel> _taskLogRepoFactory;
    readonly SaveTaskLogStat _saveTaskLogStat;
    readonly Timer _timer;

    public TaskLogPersistenceService(
        ILogger<TaskLogPersistenceService> logger,
        ApplicationRepositoryFactory<TaskLogModel> taskLogRepoFactory,
        BatchQueue<TaskLogGroup> taskLogGroupBatchQueue,
        ExceptionCounter exceptionCounter,
        WebServerCounter webServerCounter,
        IMemoryCache memoryCache)
    {
        _logger = logger;
        _taskLogRepoFactory = taskLogRepoFactory;
        _exceptionCounter = exceptionCounter;
        _taskLogGroupBatchQueue = taskLogGroupBatchQueue;
        _webServerCounter = webServerCounter;
        _memoryCache = memoryCache;
        _addedTaskLogPageDictionary = new ConcurrentDictionary<string, TaskLogModel>();
        _updatedTaskLogPageDictionary = new ConcurrentDictionary<string, TaskLogModel>();
        _saveTaskLogStat = new SaveTaskLogStat();
        _timer = new Timer(OnTimer);
        _timer.Change(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(3));
    }

    private void OnTimer(object? state)
    {
        _taskLogGroupBatchQueue.Post(new TaskLogGroup() { Id = null, LogEntries = [] });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var arrayPoolCollection in _taskLogGroupBatchQueue.ReceiveAllAsync(stoppingToken))
        {
            try
            {
                using var taskLogRepo = _taskLogRepoFactory.CreateRepository();
                var stopwatch = new Stopwatch();

                stopwatch.Restart();
                foreach (var taskLogGroups in arrayPoolCollection.GroupBy(x => x.Id))
                {
                    if (taskLogGroups.Key == null)
                    {
                        continue;
                    }
                    await SaveTaskLogGroupAsync(
                        taskLogRepo,
                        taskLogGroups,
                        stoppingToken);
                }
                stopwatch.Stop();

                _webServerCounter.TaskExecutionReportQueryLogEntriesTimeSpan += stopwatch.Elapsed;
                if (stopwatch.Elapsed > _webServerCounter.TaskExecutionReportSaveLogEntriesMaxTimeSpan)
                {
                    _webServerCounter.TaskExecutionReportSaveLogEntriesMaxTimeSpan = stopwatch.Elapsed;
                }

                stopwatch.Restart();
                await AddOrUpdateTaskLogPagesAsync(taskLogRepo, stoppingToken);
                stopwatch.Stop();

                _webServerCounter.TaskExecutionReportSaveLogEntriesTimeSpan += stopwatch.Elapsed;
                _webServerCounter.TaskExecutionReportLogEntriesSavedCount = _saveTaskLogStat.LogEntriesSaved;
                _webServerCounter.TaskExecutionReportLogEntriesPageCount = _saveTaskLogStat.PageCreated;
                _webServerCounter.TaskExecutionReportLogGroupAvailableCount = (uint)_taskLogGroupBatchQueue.AvailableCount;
                if (stopwatch.Elapsed > _webServerCounter.TaskExecutionReportSaveLogEntriesMaxTimeSpan)
                {
                    _webServerCounter.TaskExecutionReportSaveLogEntriesMaxTimeSpan = stopwatch.Elapsed;
                }
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
            finally
            {

            }
        }
    }

    async Task AddOrUpdateTaskLogPagesAsync(
        IRepository<TaskLogModel> taskLogRepo,
        CancellationToken stoppingToken = default)
    {
        if (!_addedTaskLogPageDictionary.IsEmpty)
        {
            var addedTaskLogs = _addedTaskLogPageDictionary.Values.Where(FilterTaskLog).ToArray();
            if (addedTaskLogs.Length > 0)
            {
                await taskLogRepo.AddRangeAsync(addedTaskLogs, stoppingToken);
                ResetDirtyCount(addedTaskLogs);
                RemoveFullTaskLogPages(addedTaskLogs, _addedTaskLogPageDictionary);
            }
            RemoveTaskLogPages(_addedTaskLogPageDictionary);
        }
        if (!_updatedTaskLogPageDictionary.IsEmpty)
        {
            var updatedTaskLogs = _updatedTaskLogPageDictionary.Values.Where(FilterTaskLog).ToArray();
            if (updatedTaskLogs.Length > 0)
            {
                await taskLogRepo.UpdateRangeAsync(updatedTaskLogs, stoppingToken);
                ResetDirtyCount(updatedTaskLogs);
                RemoveFullTaskLogPages(updatedTaskLogs, _updatedTaskLogPageDictionary);
            }
            RemoveTaskLogPages(_updatedTaskLogPageDictionary);
        }
    }

    private static void ResetDirtyCount(TaskLogModel[] addedTaskLogs)
    {
        if (addedTaskLogs.Length == 0)
        {
            return;
        }
        foreach (var taskLog in addedTaskLogs)
        {
            taskLog.DirtyCount = 0;
        }
    }

    async Task SaveTaskLogGroupAsync(
        IRepository<TaskLogModel> taskLogRepo,
        IGrouping<string, TaskLogGroup> taskLogGroups,
        CancellationToken stoppingToken=default)
    {
        var taskId = taskLogGroups.Key;
        var groupsCount = taskLogGroups.Count();
        var logEntries = taskLogGroups.SelectMany(static x => x.LogEntries);
        int logEntriesCount = logEntries.Count();
        var taskLogInfoKey = $"{nameof(TaskLogPersistenceService)}:{taskId}";
        if (!_memoryCache.TryGetValue<TaskLogModel>(taskLogInfoKey, out var taskInfoLog))
        {
            taskInfoLog = await taskLogRepo.FirstOrDefaultAsync(new TaskLogSpecification(taskId, 0), stoppingToken);
            _memoryCache.Set(taskLogInfoKey, taskInfoLog, TimeSpan.FromHours(1));
        }
        if (taskInfoLog == null)
        {
            taskInfoLog = new TaskLogModel()
            {
                Id = Guid.NewGuid().ToString(),
                Name = taskId,
                ActualSize = logEntriesCount,
                PageIndex = 0,
                PageSize = 1
            };

            _addedTaskLogPageDictionary.AddOrUpdate(taskInfoLog.Id, taskInfoLog, (key, oldValue) => taskInfoLog);
        }

        await SaveTaskLogAsync(
                     taskLogRepo,
                     taskId,
                     taskInfoLog,
                     logEntries,
                     logEntriesCount,
                     stoppingToken);

        _webServerCounter.TaskExecutionReportLogGroupConsumeCount += (uint)groupsCount;
    }

    async ValueTask SaveTaskLogAsync(
        IRepository<TaskLogModel> taskLogRepo,
        string taskId,
        TaskLogModel taskInfoLog,
        IEnumerable<LogEntry> logEntries,
        int logEntriesCount,
        CancellationToken stoppingToken = default)
    {

        int logIndex = 0;
        TaskLogModel? currentLogPage = null;
        while (logIndex < logEntriesCount)
        {
            if (currentLogPage == null)
            {
                currentLogPage = this._addedTaskLogPageDictionary.Values.FirstOrDefault(x => x.Name == taskId && x.PageIndex == taskInfoLog.PageSize);
            }
            if (currentLogPage == null)
            {
                currentLogPage = await taskLogRepo.FirstOrDefaultAsync(new TaskLogSpecification(taskId, taskInfoLog.PageSize, 1), stoppingToken);
            }

            if (currentLogPage == null || currentLogPage.ActualSize == currentLogPage.PageSize)
            {
                if (currentLogPage != null && currentLogPage.ActualSize == currentLogPage.PageSize)
                {
                    taskInfoLog.PageSize += 1;
                }
                currentLogPage = CreateNewLogPage(taskId, taskInfoLog);
                _saveTaskLogStat.PageCreated += 1;
                _addedTaskLogPageDictionary.AddOrUpdate(currentLogPage.Id, currentLogPage, (key, oldValue) => currentLogPage);
                _updatedTaskLogPageDictionary.AddOrUpdate(taskInfoLog.Id, taskInfoLog, (key, oldValue) => taskInfoLog);
            }
            if (currentLogPage.ActualSize < currentLogPage.PageSize)
            {
                int takeCount = Math.Min(currentLogPage.PageSize - currentLogPage.ActualSize, logEntriesCount);
                currentLogPage.LogEntries = currentLogPage.LogEntries.Union(logEntries.Skip(logIndex).Take(takeCount)).ToList();
                currentLogPage.ActualSize = currentLogPage.LogEntries.Count;
                currentLogPage.DirtyCount++;
                logIndex += takeCount;
                _saveTaskLogStat.LogEntriesSaved += (uint)takeCount;
                taskInfoLog.ActualSize += takeCount;
                taskInfoLog.DirtyCount++;
                if (!_addedTaskLogPageDictionary.ContainsKey(taskInfoLog.Id))
                {
                    _updatedTaskLogPageDictionary.AddOrUpdate(taskInfoLog.Id, taskInfoLog, (key, oldValue) => taskInfoLog);
                }
            }
            if (!_addedTaskLogPageDictionary.ContainsKey(currentLogPage.Id))
            {
                _updatedTaskLogPageDictionary.AddOrUpdate(currentLogPage.Id, currentLogPage, (key, oldValue) => currentLogPage);
            }
        }

    }

    void RemoveFullTaskLogPages(IEnumerable<TaskLogModel> taskLogs, ConcurrentDictionary<string, TaskLogModel> dict)
    {
        foreach (var taskLog in taskLogs)
        {
            if (taskLog.PageIndex > 0 && taskLog.ActualSize - taskLog.PageSize >= 0)
            {
                dict.TryRemove(taskLog.Id, out _);
                _saveTaskLogStat.PageSaved += 1;
            }
        }
    }

    void RemoveTaskLogPages(ConcurrentDictionary<string, TaskLogModel> dict)
    {
        if (dict.IsEmpty)
        {
            return;
        }
        var notUsedTaskLogPages = dict.Values.Where(FilterNotUsedTaskLogPage).ToArray();
        foreach (var taskLogPage in notUsedTaskLogPages)
        {
            dict.TryRemove(taskLogPage.Id, out _);
        }
    }

    bool FilterNotUsedTaskLogPage(TaskLogModel taskLog)
    {
        return DateTime.UtcNow - taskLog.CreationDateTime > TimeSpan.FromHours(1);
    }

    bool FilterTaskLog(TaskLogModel taskLog)
    {
        if (taskLog.DirtyCount == 0)
        {
            return false;
        }
        if (taskLog.PageIndex == 0)
        {
            return true;
        }
        return taskLog.ActualSize >= taskLog.PageSize || DateTime.UtcNow - taskLog.CreationDateTime > TimeSpan.FromSeconds(30);
    }

    private static TaskLogModel CreateNewLogPage(string taskId, TaskLogModel? taskInfoLog)
    {
        return new TaskLogModel
        {
            Id = Guid.NewGuid().ToString(),
            Name = taskId,
            PageIndex = taskInfoLog.PageSize,
            PageSize = 256,
        };
    }
}