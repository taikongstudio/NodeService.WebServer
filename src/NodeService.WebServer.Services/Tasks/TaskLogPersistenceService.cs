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

    private readonly ExceptionCounter _exceptionCounter;
    private readonly BatchQueue<TaskLogUnit> _taskLogUnitBatchQueue;
    private readonly WebServerCounter _webServerCounter;

    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<TaskLogPersistenceService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ApplicationRepositoryFactory<TaskLogModel> _taskLogRepoFactory;
    private readonly Timer _timer;
    private readonly ConcurrentDictionary<int, TaskLogHandler> _taskLogHandlers;
    private IEnumerable<int> _keys = [];

    public TaskLogPersistenceService(
        IServiceProvider serviceProvider,
        ILogger<TaskLogPersistenceService> logger,
        ApplicationRepositoryFactory<TaskLogModel> taskLogRepoFactory,
        BatchQueue<TaskLogUnit> taskLogUnitBatchQueue,
        ExceptionCounter exceptionCounter,
        WebServerCounter webServerCounter,
        IMemoryCache memoryCache)
    {
        _logger = logger;
        _taskLogRepoFactory = taskLogRepoFactory;
        _exceptionCounter = exceptionCounter;
        _taskLogUnitBatchQueue = taskLogUnitBatchQueue;
        _webServerCounter = webServerCounter;
        _memoryCache = memoryCache;
        _taskLogHandlers = new ConcurrentDictionary<int, TaskLogHandler>();
        _serviceProvider = serviceProvider;
        _timer = new Timer(OnTimer);
        _timer.Change(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
    }

    private void OnTimer(object? state)
    {
        _taskLogUnitBatchQueue.Post(new TaskLogUnit() { Id = null, LogEntries = [] });
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await foreach (var arrayPoolCollection in _taskLogUnitBatchQueue.ReceiveAllAsync(cancellationToken))
        {
            await Parallel.ForEachAsync(
                arrayPoolCollection.GroupBy(TaskLogUnitGroupFunc),
                cancellationToken,
                RunTaskLogHandlerAsync);
            _keys = _taskLogHandlers.Keys;
            Stat();
        }
    }

    private int GetActiveTaskLogGroupCount(TaskLogHandler taskLogHandler)
    {
        return taskLogHandler.ActiveTaskLogGroupCount;
    }

    private void Stat()
    {
        _webServerCounter.TaskExecutionReportLogEntriesPageCount =
            (ulong)_taskLogHandlers.Values.Sum(x => x.TotalPageCount);
        _webServerCounter.TaskExecutionReportLogGroupConsumeCount =
            (ulong)_taskLogHandlers.Values.Sum(x => x.TotalGroupConsumeCount);
        _webServerCounter.TaskExecutionReportLogEntriesSavedCount =
            (ulong)_taskLogHandlers.Values.Sum(x => x.TotalLogEntriesSavedCount);
        _webServerCounter.TaskExecutionReportLogEntriesSaveMaxTimeSpan =
            _taskLogHandlers.Values.Max(x => x.TotalSaveMaxTimeSpan);
        _webServerCounter.TaskExecutionReportLogEntriesQueryTimeSpan =
            _taskLogHandlers.Values.Max(x => x.TotalQueryTimeSpan);
        _webServerCounter.TaskExecutionReportLogEntriesSaveTimeSpan =
            _taskLogHandlers.Values.Max(x => x.TotalSaveTimeSpan);
        _webServerCounter.TaskExecutionReportLogGroupAvailableCount = (uint)_taskLogUnitBatchQueue.AvailableCount;
    }

    private async ValueTask RunTaskLogHandlerAsync(IGrouping<int, TaskLogUnit> taskLogUnitGroup,
        CancellationToken cancellationToken)
    {
        var key = taskLogUnitGroup.Key;
        var handler = _taskLogHandlers.GetOrAdd(key, CreateTaskLogHandlerFactory);
        await handler.ProcessAsync(taskLogUnitGroup, cancellationToken);
    }

    private TaskLogHandler CreateTaskLogHandlerFactory(int id)
    {
        var taskLogHandler = new TaskLogHandler(
            _serviceProvider.GetService<ILogger<TaskLogHandler>>(),
            _taskLogRepoFactory,
            _exceptionCounter)
        {
            Id = id
        };
        return taskLogHandler;
    }

    private int TaskLogUnitGroupFunc(TaskLogUnit unit)
    {
        if (unit.Id == null) return _keys.ElementAtOrDefault(Random.Shared.Next(0, _taskLogHandlers.Count));
        Math.DivRem(unit.Id[0], 10, out var result);
        return result;
    }
}