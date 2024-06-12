using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data;
using NodeService.WebServer.Services.Counters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NodeService.Infrastructure.Logging;

namespace NodeService.WebServer.Services.Tasks
{
    public class TaskLogHandler
    {
        private class TaskLogStat
        {
            public long PageCreatedCount;
            public long PageSaveCount;
            public long PageNotSavedCount;
            public long LogEntriesSavedCount;
        }

        readonly ConcurrentDictionary<string, TaskLogModel> _addedTaskLogPageDictionary;
        readonly ConcurrentDictionary<string, TaskLogModel> _updatedTaskLogPageDictionary;
        readonly ExceptionCounter _exceptionCounter;
        readonly ILogger<TaskLogHandler> _logger;
        readonly ApplicationRepositoryFactory<TaskLogModel> _taskLogRepoFactory;
        readonly TaskLogStat _taskLogStat;

        public long TotalGroupConsumeCount { get; private set; }

        public TimeSpan TotalQueryTimeSpan { get; private set; }

        public TimeSpan TotalSaveMaxTimeSpan { get; private set; }

        public TimeSpan TotalSaveTimeSpan { get; private set; }

        public long TotalLogEntriesSavedCount { get; private set; }

        public long TotalPageCount { get; private set; }

        public int ActiveTaskLogGroupCount {  get; private set; }

        public int Id { get; set; }

        public TaskLogHandler(
            ILogger<TaskLogHandler> logger,
            ApplicationRepositoryFactory<TaskLogModel> taskLogRepoFactory,
            ExceptionCounter exceptionCounter)
        {
            _logger = logger;
            _taskLogRepoFactory = taskLogRepoFactory;
            _addedTaskLogPageDictionary = new ConcurrentDictionary<string, TaskLogModel>();
            _updatedTaskLogPageDictionary = new ConcurrentDictionary<string, TaskLogModel>();
            _exceptionCounter = exceptionCounter;
            _taskLogStat = new TaskLogStat();
        }

        public async ValueTask ProcessAsync(
            IEnumerable<TaskLogGroup> taskLogGroupList,
            CancellationToken stoppingToken = default)
        {
            try
            {
                var stopwatch = new Stopwatch();

                stopwatch.Restart();
                foreach (var taskLogGroups in taskLogGroupList.GroupBy(x => x.Id))
                {
                    if (taskLogGroups.Key == null)
                    {
                        continue;
                    }
                    await SaveTaskLogGroupAsync(
                        taskLogGroups,
                        stoppingToken);
                }
                stopwatch.Stop();

                TotalQueryTimeSpan += stopwatch.Elapsed;
                if (stopwatch.Elapsed > TotalSaveMaxTimeSpan)
                {
                    TotalSaveMaxTimeSpan = stopwatch.Elapsed;
                }

                stopwatch.Restart();
                await AddOrUpdateTaskLogPagesAsync(stoppingToken);
                stopwatch.Stop();

                TotalSaveTimeSpan += stopwatch.Elapsed;
                TotalLogEntriesSavedCount = _taskLogStat.LogEntriesSavedCount;
                TotalPageCount = _taskLogStat.PageCreatedCount;
                if (stopwatch.Elapsed > TotalSaveMaxTimeSpan)
                {
                    TotalSaveMaxTimeSpan = stopwatch.Elapsed;
                }
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

        async Task AddOrUpdateTaskLogPagesAsync(
            CancellationToken stoppingToken = default)
        {
            using var taskLogRepo = _taskLogRepoFactory.CreateRepository();
            if (!_addedTaskLogPageDictionary.IsEmpty)
            {
                var addedTaskLogs = _addedTaskLogPageDictionary.Values.ToArray();

                foreach (var taskLog in addedTaskLogs)
                {
                    await taskLogRepo.AddAsync(taskLog, stoppingToken);
                }
                ResetTaskLogPageDirtyCount(addedTaskLogs);
                MoveToUpdateDictionary(addedTaskLogs);

            }
            if (!_updatedTaskLogPageDictionary.IsEmpty)
            {
                var updatedTaskLogs = _updatedTaskLogPageDictionary.Values.Where(IsTaskLogPageChanged).ToArray();
                if (updatedTaskLogs.Length > 0)
                {
                    foreach (var taskLog in updatedTaskLogs)
                    {
                        await taskLogRepo.UpdateAsync(taskLog, stoppingToken);
                    }

                    ResetTaskLogPageDirtyCount(updatedTaskLogs);
                }
                RemoveFullTaskLogPages(_updatedTaskLogPageDictionary);
                RemoveInactiveTaskLogPages(_updatedTaskLogPageDictionary);
            }
        }

        void MoveToUpdateDictionary(TaskLogModel[] taskLogPages)
        {
            if (taskLogPages.Length == 0)
            {
                return;
            }
            foreach (var taskLogPage in taskLogPages)
            {
                if (IsFullTaskLogPage(taskLogPage))
                {
                    _taskLogStat.PageSaveCount++;
                    continue;
                }
                _updatedTaskLogPageDictionary.AddOrUpdate(
                    taskLogPage.Id,
                    taskLogPage,
                    (key, oldValue) => taskLogPage);
            }
            _addedTaskLogPageDictionary.Clear();
        }

        static void ResetTaskLogPageDirtyCount(TaskLogModel[] taskLogs)
        {
            if (taskLogs.Length == 0)
            {
                return;
            }
            foreach (var taskLog in taskLogs)
            {
                taskLog.DirtyCount = 0;
            }
        }

        async Task SaveTaskLogGroupAsync(
            IGrouping<string, TaskLogGroup> taskLogGroups,
            CancellationToken stoppingToken = default)
        {
            var taskId = taskLogGroups.Key;
            var groupsCount = taskLogGroups.Count();
            var logEntries = taskLogGroups.SelectMany(static x => x.LogEntries);
            var logEntriesCount = logEntries.Count();

            TaskLogModel? taskInfoLog = await EnsureTaskInfoLogAsync(taskId, stoppingToken);
            if (taskInfoLog == null)
            {
                return;
            }

            await SaveTaskLogPagesAsync(
                         taskId,
                         taskInfoLog,
                         logEntries,
                         logEntriesCount,
                         stoppingToken);

            TotalGroupConsumeCount += (uint)groupsCount;
        }

        async Task<TaskLogModel?> EnsureTaskInfoLogAsync(
            string taskId,
            CancellationToken stoppingToken = default)
        {
            var taskLogInfoKey = CreateTaskLogInfoKey(taskId);
            if (!_updatedTaskLogPageDictionary.TryGetValue(taskLogInfoKey, out var taskInfoLog) || taskInfoLog == null)
            {
                var taskLogPageIdLike = $"{taskId}_%";
                var taskLogInfoPageId = $"{taskId}_0";
                using var taskLogRepo = _taskLogRepoFactory.CreateRepository();
                var dbContext = taskLogRepo.DbContext as ApplicationDbContext;
                if (await dbContext.TaskLogDbSet.AnyAsync(x => x.Id.StartsWith(taskId)))
                {
                    var actualSize = await dbContext.TaskLogDbSet.Where(x => x.Id.StartsWith(taskId) && x.PageIndex >= 1).SumAsync(x => x.ActualSize);
                    int pageSize = await dbContext.TaskLogDbSet.Where(x => x.Id.StartsWith(taskId) && x.PageIndex >= 1).MaxAsync(x => x.PageIndex);
                    FormattableString sql = $"update TaskLogDbSet\r\nset ActualSize={actualSize},\r\nPageSize={pageSize},\r\nPageIndex=0\r\nwhere Id={taskLogInfoPageId}";
                    var changesCount = await taskLogRepo.DbContext.Database.ExecuteSqlAsync(
                                    sql,
                                    cancellationToken: stoppingToken);
                    taskInfoLog = await taskLogRepo.FirstOrDefaultAsync(new TaskLogSpecification(taskId, 0), stoppingToken);
                }
            }
            if (taskInfoLog == null)
            {
                taskInfoLog = CreateTaskLogInfoPage(taskId);

                _addedTaskLogPageDictionary.AddOrUpdate(taskInfoLog.Id, taskInfoLog, (key, oldValue) => taskInfoLog);
            }

            return taskInfoLog;
        }

        async ValueTask SaveTaskLogPagesAsync(
            string taskId,
            TaskLogModel taskInfoLog,
            IEnumerable<LogEntry> logEntries,
            int logEntriesCount,
            CancellationToken stoppingToken = default)
        {

            int logIndex = 0;
            TaskLogModel? currentLogPage = null;
            var key = $"{taskId}_{taskInfoLog.PageSize}";
            if (!_updatedTaskLogPageDictionary.TryGetValue(key, out currentLogPage) || currentLogPage == null)
            {
                using var taskLogRepo = _taskLogRepoFactory.CreateRepository();
                currentLogPage = await taskLogRepo.FirstOrDefaultAsync(new TaskLogSpecification(taskId, taskInfoLog.PageSize), stoppingToken);
            }
            while (logIndex < logEntriesCount)
            {
                if (currentLogPage == null || currentLogPage.ActualSize == currentLogPage.PageSize)
                {
                    if (currentLogPage != null && currentLogPage.ActualSize == currentLogPage.PageSize)
                    {
                        taskInfoLog.PageSize += 1;
                        _updatedTaskLogPageDictionary.AddOrUpdate(taskInfoLog.Id, taskInfoLog, (key, oldValue) => taskInfoLog);
                    }
                    currentLogPage = CreateNewTaskLogPage(
                        taskId,
                        taskInfoLog.PageSize,
                        256);
                    _taskLogStat.PageCreatedCount += 1;
                    _addedTaskLogPageDictionary.AddOrUpdate(currentLogPage.Id, currentLogPage, (key, oldValue) => currentLogPage);
                }
                if (currentLogPage.ActualSize < currentLogPage.PageSize)
                {
                    int takeCount = Math.Min(currentLogPage.PageSize - currentLogPage.ActualSize, logEntriesCount);
                    currentLogPage.Value.LogEntries = currentLogPage.Value.LogEntries.Union(logEntries.Skip(logIndex).Take(takeCount)).ToList();
                    currentLogPage.ActualSize = currentLogPage.Value.LogEntries.Count;
                    currentLogPage.DirtyCount++;
                    currentLogPage.LastWriteTime = DateTime.UtcNow;
                    logIndex += takeCount;
                    _taskLogStat.LogEntriesSavedCount += (uint)takeCount;
                    taskInfoLog.ActualSize += takeCount;
                    taskInfoLog.DirtyCount++;
                    taskInfoLog.LastWriteTime = DateTime.UtcNow;
                    _updatedTaskLogPageDictionary.AddOrUpdate(taskInfoLog.Id, taskInfoLog, (key, oldValue) => taskInfoLog);
                    _updatedTaskLogPageDictionary.AddOrUpdate(currentLogPage.Id, currentLogPage, (key, oldValue) => currentLogPage);
                }
            }

        }

        void RemoveFullTaskLogPages(ConcurrentDictionary<string, TaskLogModel> dict)
        {
            if (dict.IsEmpty)
            {
                return;
            }
            var fullTaskLogPages = dict.Values.Where(IsFullTaskLogPage).ToArray();
            if (fullTaskLogPages.Length > 0)
            {
                foreach (var taskLogPage in fullTaskLogPages)
                {
                    _taskLogStat.PageSaveCount++;
                    dict.TryRemove(taskLogPage.Id, out _);
                }
            }
        }

        void RemoveInactiveTaskLogPages(ConcurrentDictionary<string, TaskLogModel> dict)
        {
            if (dict.IsEmpty)
            {
                return;
            }
            var inactiveTaskLogPages = dict.Values.Where(IsInactiveTaskLogPage).ToArray();
            if (inactiveTaskLogPages.Length > 0)
            {
                foreach (var taskLogPage in inactiveTaskLogPages)
                {
                    dict.TryRemove(taskLogPage.Id, out _);
                }
            }
        }

        bool IsFullTaskLogPage(TaskLogModel taskLogPage)
        {
            return taskLogPage.PageIndex > 0 && taskLogPage.ActualSize >= taskLogPage.PageSize;
        }

        bool IsInactiveTaskLogPage(TaskLogModel taskLog)
        {
            return DateTime.UtcNow - taskLog.LastWriteTime > TimeSpan.FromHours(1);
        }

        bool IsTaskLogPageChanged(TaskLogModel taskLog)
        {
            if (taskLog.DirtyCount == 0)
            {
                return false;
            }
            if (taskLog.PageIndex == 0)
            {
                return DateTime.UtcNow - taskLog.LastWriteTime > TimeSpan.FromSeconds(5);
            }
            return taskLog.ActualSize >= taskLog.PageSize || DateTime.UtcNow - taskLog.LastWriteTime > TimeSpan.FromSeconds(10);
        }

        static TaskLogModel CreateNewTaskLogPage(string taskId, int pageIndex, int pageSize)
        {
            return new TaskLogModel
            {
                Id = $"{taskId}_{pageIndex}",
                PageIndex = pageIndex,
                PageSize = pageSize,
            };
        }

        static string CreateTaskLogInfoKey(string taskId)
        {
            return $"{taskId}_0";
        }

        static TaskLogModel CreateTaskLogInfoPage(string taskId)
        {
            return new TaskLogModel()
            {
                Id = CreateTaskLogInfoKey(taskId),
                ActualSize = 0,
                PageIndex = 0,
                PageSize = 1
            };
        }
    }
}
