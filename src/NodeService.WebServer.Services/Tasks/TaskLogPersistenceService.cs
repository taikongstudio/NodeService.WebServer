using Microsoft.Extensions.Hosting;
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
                foreach (var taskLogGroup in arrayPoolCollection)
                {
                    stopwatch.Restart();
                    var saveLogStat = await SaveTaskLogAsync(
                        taskLogRepo,
                        taskLogGroup,
                        stoppingToken);
                    stopwatch.Stop();
                    _webServerCounter.TaskExecutionReportSaveLogEntriesTimeSpan += stopwatch.Elapsed;
                    _webServerCounter.TaskExecutionReportLogEntriesCount += (uint)taskLogGroup.LogEntries.Length;
                    _webServerCounter.TaskExecutionReportLogEntriesPageCount += saveLogStat.PageCreated;
                    if (stopwatch.Elapsed > _webServerCounter.TaskExecutionReportSaveLogEntriesMaxTimeSpan)
                    {
                        _webServerCounter.TaskExecutionReportSaveLogEntriesMaxTimeSpan = stopwatch.Elapsed;
                    }
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
        CancellationToken stoppingToken = default)
    {
        var saveLogStat = new SaveLogStat();
        int logCount = taskLogGroup.LogEntries.Length;
        int logIndex = 0;
        var addedTaskLogPageList = new List<TaskLogModel>();
        var updatedTaskLogPageList = new List<TaskLogModel>();
        while (logIndex < logCount)
        {
            var taskLog = await taskLogRepo.FirstOrDefaultAsync(new TaskLogSpecification(taskLogGroup.Id), stoppingToken);
            if (taskLog == null || taskLog.ActualSize == taskLog.PageSize)
            {
                int lastPageIndex = taskLog?.PageIndex ?? 0;
                taskLog = new TaskLogModel
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = taskLogGroup.Id,
                    PageIndex = lastPageIndex + 1,
                    PageSize = 512,
                };
                saveLogStat.PageCreated += 1;
                addedTaskLogPageList.Add(taskLog);
            }
            if (taskLog.ActualSize < taskLog.PageSize)
            {
                int takeCount = Math.Min(taskLog.PageSize - taskLog.ActualSize, taskLogGroup.LogEntries.Length);
                taskLog.LogEntries = taskLog.LogEntries.Union(taskLogGroup.LogEntries.Skip(logIndex).Take(takeCount)).ToList();
                taskLog.ActualSize = taskLog.LogEntries.Count;
                logIndex += takeCount;
                saveLogStat.LogEntriesWritten += (uint)takeCount;
            }
            updatedTaskLogPageList.Add(taskLog);
        }

        var taskInfoLog = await taskLogRepo.FirstOrDefaultAsync(
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
                PageSize = 0
            };
            addedTaskLogPageList.Add(taskInfoLog);
        }
        else
        {
            taskInfoLog.ActualSize += logCount;
            updatedTaskLogPageList.Add(taskInfoLog);
        }

        await taskLogRepo.AddRangeAsync(addedTaskLogPageList, stoppingToken);
        await taskLogRepo.UpdateRangeAsync(updatedTaskLogPageList, stoppingToken);

        return saveLogStat;
    }

}