using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Logging;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using System.Linq;

namespace NodeService.WebServer.Services.Tasks;

public class TaskLogStorageHandler
{
    private class TaskLogStat
    {
        public long PageCreatedCount;
        public long PageSavedCount;
        public long PageNotSavedCount;
        public long LogEntriesSavedCount;
        public long PageDetachedCount;
    }

    const int PAGESIZE = 1024;

    readonly ConcurrentDictionary<TaskLogPageKey, TaskLogModel> _addedTaskLogPageDictionary;
    readonly ConcurrentDictionary<TaskLogPageKey, TaskLogModel> _updatedTaskLogPageDictionary;
    readonly ExceptionCounter _exceptionCounter;
    readonly ILogger<TaskLogStorageHandler> _logger;
    readonly ApplicationRepositoryFactory<TaskLogModel> _taskLogRepoFactory;
    readonly TaskLogStat _taskLogStat;

    public long TotalGroupConsumeCount { get; private set; }

    public TimeSpan TotalQueryTimeSpan { get; private set; }

    public TimeSpan TotalSaveMaxTimeSpan { get; private set; }

    public TimeSpan TotalSaveTimeSpan { get; private set; }

    public long TotalLogEntriesSavedCount { get; private set; }

    public long TotalCreatedPageCount { get; private set; }

    public long TotalDetachedPageCount { get; private set; }

    public int ActiveTaskLogGroupCount { get; private set; }

    public int Id { get; set; }

    public TaskLogStorageHandler(
        ILogger<TaskLogStorageHandler> logger,
        ApplicationRepositoryFactory<TaskLogModel> taskLogRepoFactory,
        ExceptionCounter exceptionCounter)
    {
        _logger = logger;
        _taskLogRepoFactory = taskLogRepoFactory;
        _addedTaskLogPageDictionary = new ConcurrentDictionary<TaskLogPageKey, TaskLogModel>();
        _updatedTaskLogPageDictionary = new ConcurrentDictionary<TaskLogPageKey, TaskLogModel>();
        _exceptionCounter = exceptionCounter;
        _taskLogStat = new TaskLogStat();
    }

    public async ValueTask ProcessAsync(
        IEnumerable<TaskLogUnit> taskLogUnits,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stopwatch = new Stopwatch();

            stopwatch.Restart();
            foreach (var taskLogUnitGroups in taskLogUnits.GroupBy(GetTaskLogUnitKey))
            {
                if (taskLogUnitGroups.Key == null) continue;
                await ProcessTaskLogUnitGroupAsync(
                    taskLogUnitGroups,
                    cancellationToken);
            }

            stopwatch.Stop();

            TotalQueryTimeSpan += stopwatch.Elapsed;
            if (stopwatch.Elapsed > TotalSaveMaxTimeSpan) TotalSaveMaxTimeSpan = stopwatch.Elapsed;

            stopwatch.Restart();
            await AddOrUpdateTaskLogPagesAsync(cancellationToken);
            stopwatch.Stop();

            TotalSaveTimeSpan += stopwatch.Elapsed;
            TotalLogEntriesSavedCount = _taskLogStat.LogEntriesSavedCount;
            TotalCreatedPageCount = _taskLogStat.PageCreatedCount;
            TotalDetachedPageCount = _taskLogStat.PageDetachedCount;
            if (stopwatch.Elapsed > TotalSaveMaxTimeSpan) TotalSaveMaxTimeSpan = stopwatch.Elapsed;
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
        finally
        {
            ActiveTaskLogGroupCount = _updatedTaskLogPageDictionary.Count;
        }
    }

    private static string GetTaskLogUnitKey(TaskLogUnit taskLogUnit)
    {
        return taskLogUnit.Id;
    }

    async ValueTask AddOrUpdateTaskLogPagesAsync(CancellationToken cancellationToken = default)
    {
        if (_updatedTaskLogPageDictionary.IsEmpty && _addedTaskLogPageDictionary.IsEmpty)
        {
            return;
        }
        using var taskLogRepo = _taskLogRepoFactory.CreateRepository();
        Stopwatch stopwatch = Stopwatch.StartNew();
        var updateCount = 0;
        var addedCount = 0;
        if (!_updatedTaskLogPageDictionary.IsEmpty)
        {
            var changedLogs = _updatedTaskLogPageDictionary.Values.Where(TaskLogModelExtensions.IsTaskLogPageChanged).ToArray();
            updateCount = changedLogs.Length;
            var changedLogInfoPageGroups = changedLogs.GroupBy(static x => x.GetKey().TaskExecutionInstanceId);
            foreach (var pageGroups in changedLogInfoPageGroups)
            {
                await UpdateTaskLogsAsync(pageGroups, taskLogRepo.UpdateAsync, cancellationToken);
            }
        }
        if (!_addedTaskLogPageDictionary.IsEmpty)
        {
            var addedTaskLogs = _addedTaskLogPageDictionary.Values;
            updateCount = addedTaskLogs.Count;
            var addedTaskLogPageGroups = addedTaskLogs.GroupBy(static x => x.GetKey().TaskExecutionInstanceId);
            foreach (var taskLogPageGroup in addedTaskLogPageGroups)
            {
                await AddTaskLogsAsync(taskLogPageGroup, taskLogRepo.AddAsync, cancellationToken);
            }
            CleanupDictionary(addedTaskLogs);
        }
        if (!_updatedTaskLogPageDictionary.IsEmpty)
        {
            RemoveFullTaskLogPages(_updatedTaskLogPageDictionary);
            RemoveInactiveTaskLogPages(_updatedTaskLogPageDictionary);
        }
        stopwatch.Stop();
        _logger.LogInformation($"Update {updateCount},Add{addedCount},spend:{stopwatch.Elapsed}");
    }

    async ValueTask UpdateTaskLogsAsync(IEnumerable<TaskLogModel> taskLogPages, Func<TaskLogModel, CancellationToken, Task> updateFunc, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!taskLogPages.Any())
            {
                return;
            }
            foreach (var taskLogPage in taskLogPages)
            {
                try
                {
                    await updateFunc(taskLogPage, cancellationToken);
                    taskLogPage.DirtyCount = 0;
                    taskLogPage.IsCommited = true;
                    _logger.LogInformation($"Update task log:{taskLogPage.Id}");
                }
                catch (DbUpdateException ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }
                catch (Exception ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }

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

    async ValueTask AddTaskLogsAsync(IEnumerable<TaskLogModel> taskLogPages, Func<TaskLogModel, CancellationToken, Task> addFunc, CancellationToken cancellationToken = default)
    {
        try
        {

            foreach (var taskLogPage in taskLogPages)
            {
                try
                {
                    await addFunc(taskLogPage, cancellationToken);
                    taskLogPage.DirtyCount = 0;
                    taskLogPage.IsCommited = true;
                    _logger.LogInformation($"Add task log:{taskLogPage.Id}");
                }
                catch (DbUpdateException ex)
                {

                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }
                catch (Exception ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }

            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    void CleanupDictionary(IEnumerable<TaskLogModel> taskLogPages)
    {
        if (!taskLogPages.Any()) return;
        foreach (var taskLogPage in taskLogPages)
        {
            if (taskLogPage.DirtyCount > 0)
            {
                continue;
            }
            var taskLogPageKey = taskLogPage.GetKey();
            if (taskLogPage.IsFullTaskLogPage())
            {
                _taskLogStat.PageSavedCount++;
            }
            else
            {
                _updatedTaskLogPageDictionary.TryAdd(taskLogPageKey, taskLogPage);
            }
            _addedTaskLogPageDictionary.TryRemove(taskLogPageKey, out _);
        }
    }

    async Task ProcessTaskLogUnitGroupAsync(
        IGrouping<string, TaskLogUnit> taskLogGroups,
        CancellationToken cancellationToken = default)
    {
        var taskExecutionInstanceId = taskLogGroups.Key;
        var groupsCount = taskLogGroups.Count();
        var logEntries = taskLogGroups.SelectMany(static x => x.LogEntries);
        var logEntriesCount = logEntries.Count();

        var taskInfoLog = await EnsureTaskInfoLogAsync(taskExecutionInstanceId, cancellationToken);
        if (taskInfoLog == null) return;

        await ProcessTaskLogPagesAsync(
            taskExecutionInstanceId,
            taskInfoLog,
            logEntries,
            logEntriesCount,
            cancellationToken);

        TotalGroupConsumeCount += (uint)groupsCount;
    }

    async ValueTask<TaskLogModel?> EnsureTaskInfoLogAsync(
       string taskExecutionInstanceId,
       CancellationToken cancellationToken = default)
    {
        var taskLogInfoKey = TaskLogModelExtensions.CreateTaskLogInfoPageKey(taskExecutionInstanceId);
        if (!_updatedTaskLogPageDictionary.TryGetValue(taskLogInfoKey, out var taskLogInfoPage) || taskLogInfoPage == null)
        {
            using var taskLogRepo = _taskLogRepoFactory.CreateRepository();
            taskLogRepo.DbContext.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            taskLogInfoPage = await taskLogRepo.GetByIdAsync(taskExecutionInstanceId, cancellationToken);

            if (taskLogInfoPage != null)
            {
                taskLogInfoPage.IsCommited = true;
                var actualSize = await taskLogRepo.DbContext.Set<TaskLogModel>().Where(x => x.TaskExecutionInstanceId == taskExecutionInstanceId).SumAsync(x => x.ActualSize, cancellationToken: cancellationToken);
                UpdateTaskInfoLogPage(taskLogInfoPage, actualSize, isAppend: false);
                _updatedTaskLogPageDictionary.TryAdd(taskLogInfoKey, taskLogInfoPage);
            }
        }

        if (taskLogInfoPage == null)
        {
            taskLogInfoPage = CreateTaskLogInfoPage(taskExecutionInstanceId);
            _addedTaskLogPageDictionary.TryAdd(taskLogInfoKey, taskLogInfoPage);
        }

        return taskLogInfoPage;
    }

    private static LogEntry Convert(TaskExecutionLogEntry taskExecutionLogEntry)
    {
        return new LogEntry
        {
            DateTimeUtc = taskExecutionLogEntry.DateTime.ToDateTime().ToUniversalTime(),
            Type = (int)taskExecutionLogEntry.Type,
            Value = taskExecutionLogEntry.Value
        };
    }

    async ValueTask ProcessTaskLogPagesAsync(
        string taskExecutionInstanceId,
        TaskLogModel taskInfoLog,
        IEnumerable<TaskExecutionLogEntry> logEntries,
        int logEntriesCount,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"Process {taskExecutionInstanceId}");
        var logIndex = 0;
        var currentLogPageKey = TaskLogModelExtensions.CreateTaskLogPageKey(taskExecutionInstanceId, taskInfoLog.PageSize);
        if (!_updatedTaskLogPageDictionary.TryGetValue(currentLogPageKey, out var currentLogPage) || currentLogPage == null)
        {
            using var taskLogRepo = _taskLogRepoFactory.CreateRepository();
            taskLogRepo.DbContext.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            currentLogPage = await taskLogRepo.FirstOrDefaultAsync(new TaskLogSelectSpecification<TaskLogModel>(taskExecutionInstanceId, taskInfoLog.PageSize), cancellationToken);
            if (currentLogPage != null)
            {
                currentLogPage.IsCommited = true;
                _updatedTaskLogPageDictionary.TryAdd(currentLogPageKey, currentLogPage);
            }
        }

        while (logIndex < logEntriesCount)
        {
            if (currentLogPage == null)
            {
                currentLogPage = CreateNewTaskLogPage(taskExecutionInstanceId, taskInfoLog);
            }
            else if (currentLogPage.ActualSize == PAGESIZE)
            {
                taskInfoLog.PageSize += 1;
                currentLogPage = CreateNewTaskLogPage(taskExecutionInstanceId, taskInfoLog);
            }
            var takeCount = Math.Min(currentLogPage.PageSize - currentLogPage.ActualSize, logEntriesCount);
            var takeItems = logEntries.Skip(logIndex).Take(takeCount).ToArray();
            if (AppendEntriesToTaskLogPage(currentLogPage, takeItems))
            {
                logIndex += takeItems.Length;
                UpdateTaskInfoLogPage(taskInfoLog, takeItems.Length);
                _taskLogStat.LogEntriesSavedCount += (uint)takeItems.Length;
                _logger.LogInformation($"{taskExecutionInstanceId} append {takeItems.Length}");
            }
        }

    }

    void UpdateTaskInfoLogPage(TaskLogModel taskInfoLog, int length = 0, bool isAppend = true)
    {
        if (isAppend)
        {
            taskInfoLog.ActualSize += length;
        }
        else
        {
            taskInfoLog.ActualSize = length;
        }

        taskInfoLog.DirtyCount++;
        taskInfoLog.PageSize = Math.DivRem(taskInfoLog.ActualSize, PAGESIZE, out int result);
        if (result > 0)
        {
            taskInfoLog.PageSize += 1;
        }
        taskInfoLog.LastWriteTime = DateTime.UtcNow;
        AddOrUpdateTaskLogPage(taskInfoLog);
    }

    bool AppendEntriesToTaskLogPage(TaskLogModel currentLogPage, TaskExecutionLogEntry[] logEntriesChunk)
    {
        if (currentLogPage.ActualSize >= currentLogPage.PageSize)
        {
            return false;
        }
        if (currentLogPage.ActualSize > 0)
        {
            currentLogPage.Value.LogEntries = currentLogPage.Value.LogEntries.Union(logEntriesChunk.Select(Convert));
        }
        else
        {
            currentLogPage.Value.LogEntries = logEntriesChunk.Select(Convert);
        }
        currentLogPage.ActualSize += logEntriesChunk.Length;
        currentLogPage.DirtyCount++;
        currentLogPage.LastWriteTime = DateTime.UtcNow;
        AddOrUpdateTaskLogPage(currentLogPage);

        return true;
    }

    void AddOrUpdateTaskLogPage(TaskLogModel currentLogPage)
    {
        if (currentLogPage.IsCommited)
        {
            _updatedTaskLogPageDictionary.AddOrUpdate(currentLogPage.GetKey(), currentLogPage, (key, oldValue) => currentLogPage);
        }
        else
        {
            _addedTaskLogPageDictionary.AddOrUpdate(currentLogPage.GetKey(), currentLogPage, (key, oldValue) => currentLogPage);
        }
    }

    TaskLogModel CreateNewTaskLogPage(string taskExecutionInstanceId, TaskLogModel taskInfoLog)
    {
        var currentLogPage = CreateNewTaskLogPage(
                                        taskExecutionInstanceId,
                                        taskInfoLog.PageSize,
                                        PAGESIZE);
        currentLogPage.IsCommited = false;
        _taskLogStat.PageCreatedCount += 1;
        _addedTaskLogPageDictionary.TryAdd(currentLogPage.GetKey(), currentLogPage);
        _logger.LogInformation($"{taskExecutionInstanceId} Create new page {currentLogPage.PageIndex}");
        return currentLogPage;
    }

    void RemoveFullTaskLogPages(ConcurrentDictionary<TaskLogPageKey, TaskLogModel> dict)
    {
        if (dict.IsEmpty) return;
        foreach (var taskLogPage in dict.Values)
        {
            if (taskLogPage.IsFullTaskLogPage())
            {
                _taskLogStat.PageSavedCount++;
                dict.TryRemove(taskLogPage.GetKey(), out _);
            }
        }
    }

    void RemoveInactiveTaskLogPages(ConcurrentDictionary<TaskLogPageKey, TaskLogModel> dict)
    {
        if (dict.IsEmpty) return;
        foreach (var taskLogPage in dict.Values)
        {
            if (taskLogPage.IsInactiveTaskLogPage())
            {
                dict.TryRemove(taskLogPage.GetKey(), out _);
                _taskLogStat.PageDetachedCount++;
            }
        }
    }




    static TaskLogModel CreateNewTaskLogPage(string taskExecutionInstanceId, int pageIndex, int pageSize)
    {
        return new TaskLogModel
        {
            Id = $"{taskExecutionInstanceId}_{pageIndex}",
            Name = taskExecutionInstanceId,
            TaskExecutionInstanceId = taskExecutionInstanceId,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }



    static TaskLogModel CreateTaskLogInfoPage(string taskExecutionInstanceId)
    {
        return new TaskLogModel()
        {
            Id = taskExecutionInstanceId,
            Name = "taskExecutionInstanceId",
            ActualSize = 0,
            PageIndex = 0,
            PageSize = 1
        };
    }
}