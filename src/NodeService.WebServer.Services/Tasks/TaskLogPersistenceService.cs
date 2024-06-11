using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Logging;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using System.Linq;

namespace NodeService.WebServer.Services.Tasks;

public class TaskLogPersistenceService : BackgroundService
{
    private record struct TaskLogPageKey
    {
        public TaskLogPageKey(string taskId, int pageIndex)
        {
            TaskId = taskId;
            PageIndex = pageIndex;
        }

        public string TaskId { get; private set; }

        public int PageIndex { get; private set; }
    }

    readonly ExceptionCounter _exceptionCounter;
    readonly BatchQueue<TaskLogGroup> _taskLogGroupBatchQueue;
    readonly WebServerCounter _webServerCounter;

    readonly IMemoryCache _memoryCache;
    readonly ILogger<TaskLogPersistenceService> _logger;
    readonly IServiceProvider _serviceProvider;
    readonly ApplicationRepositoryFactory<TaskLogModel> _taskLogRepoFactory;
    readonly Timer _timer;
    readonly ConcurrentDictionary<int, TaskLogHandler> _taskLogHandlers;

    public TaskLogPersistenceService(
        IServiceProvider serviceProvider,
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
        _taskLogHandlers = new ConcurrentDictionary<int, TaskLogHandler>();
        _serviceProvider = serviceProvider;
        _timer = new Timer(OnTimer);
        _timer.Change(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
    }

    void OnTimer(object? state)
    {
        _taskLogGroupBatchQueue.Post(new TaskLogGroup() { Id = null, LogEntries = [] });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var arrayPoolCollection in _taskLogGroupBatchQueue.ReceiveAllAsync(stoppingToken))
        {
            await Parallel.ForEachAsync(arrayPoolCollection.GroupBy(GroupTaskLogGroupFunc), RunTaskLogHandlerAsync);
            Stat();
        }
    }

    void Stat()
    {
        _webServerCounter.TaskExecutionReportLogEntriesPageCount = (ulong)_taskLogHandlers.Values.Sum(x => x.TotalPageCount);
        _webServerCounter.TaskExecutionReportLogGroupConsumeCount = (ulong)_taskLogHandlers.Values.Sum(x => x.TotalGroupConsumeCount);
        _webServerCounter.TaskExecutionReportLogEntriesSavedCount = (ulong)_taskLogHandlers.Values.Sum(x => x.TotalLogEntriesSavedCount);
        _webServerCounter.TaskExecutionReportLogEntriesSaveMaxTimeSpan = _taskLogHandlers.Values.Max(x => x.TotalSaveMaxTimeSpan);
        _webServerCounter.TaskExecutionReportLogEntriesQueryTimeSpan = _taskLogHandlers.Values.Max(x => x.TotalQueryTimeSpan);
        _webServerCounter.TaskExecutionReportLogEntriesSaveTimeSpan = _taskLogHandlers.Values.Max(x => x.TotalSaveTimeSpan);
        _webServerCounter.TaskExecutionReportLogGroupAvailableCount = (uint)_taskLogGroupBatchQueue.AvailableCount;
    }

    public async ValueTask RunTaskLogHandlerAsync(IGrouping<int, TaskLogGroup> taskLogGroup, CancellationToken stoppingToken)
    {
        var key = taskLogGroup.Key;
        var handler = _taskLogHandlers.GetOrAdd(key, CreateTaskLogHandler);
        await handler.ProcessAsync(taskLogGroup, stoppingToken);

    }

    TaskLogHandler CreateTaskLogHandler(int index)
    {
        return _serviceProvider.GetService<TaskLogHandler>();
    }

    private int GroupTaskLogGroupFunc(TaskLogGroup group)
    {
        if (group.Id == null)
        {
            return 0;
        }
        Math.DivRem(group.Id.GetHashCode(), 10, out int result);
        return result;
    }



}