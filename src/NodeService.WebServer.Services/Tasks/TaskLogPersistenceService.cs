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
using System.Threading.Tasks;

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
        _timer.Change(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
    }

    private void OnTimer(object? state)
    {
        _taskLogUnitBatchQueue.Post(new TaskLogUnit() { Id = null, LogEntries = [] });
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await foreach (var array in _taskLogUnitBatchQueue.ReceiveAllAsync(cancellationToken))
        {
            try
            {
                if (array == null)
                {
                    continue;
                }
                foreach (var item in array)
                {
                    if (item == null || item.Id == null)
                    {
                        continue;
                    }
                    _logger.LogInformation($"Recieve task log unit:{item.Id}");
                }
                await Parallel.ForEachAsync(
                    array.GroupBy(TaskLogUnitGroupFunc),
                    cancellationToken,
                    RunTaskLogHandlerAsync);
                _keys = _taskLogHandlers.Keys;
                Stat();
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }

        }
    }

    void Stat()
    {
        _webServerCounter.TaskLogPageCount =
            (ulong)_taskLogHandlers.Values.Sum(x => x.TotalPageCount);
        _webServerCounter.TaskLogUnitConsumeCount =
            (ulong)_taskLogHandlers.Values.Sum(x => x.TotalGroupConsumeCount);
        _webServerCounter.TaskLogEntriesSavedCount =
            (ulong)_taskLogHandlers.Values.Sum(x => x.TotalLogEntriesSavedCount);
        _webServerCounter.TaskLogUnitSaveMaxTimeSpan =
            _taskLogHandlers.Values.Max(x => x.TotalSaveMaxTimeSpan);
        _webServerCounter.TaskLogUnitQueryTimeSpan =
            _taskLogHandlers.Values.Max(x => x.TotalQueryTimeSpan);
        _webServerCounter.TaskLogUnitSaveTimeSpan =
            _taskLogHandlers.Values.Max(x => x.TotalSaveTimeSpan);
        _webServerCounter.TaskLogUnitAvailableCount = (uint)_taskLogUnitBatchQueue.AvailableCount;
    }

    async ValueTask RunTaskLogHandlerAsync(IGrouping<int, TaskLogUnit> taskLogUnitGroup,
        CancellationToken cancellationToken)
    {
        var key = taskLogUnitGroup.Key;
        var handler = _taskLogHandlers.GetOrAdd(key, CreateTaskLogHandlerFactory);
        await handler.ProcessAsync(taskLogUnitGroup, cancellationToken);
    }

    TaskLogHandler CreateTaskLogHandlerFactory(int id)
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

    int TaskLogUnitGroupFunc(TaskLogUnit unit)
    {
        if (unit.Id == null) return _keys.ElementAtOrDefault(Random.Shared.Next(0, _taskLogHandlers.Count));
        Math.DivRem(unit.Id[0], 10, out var result);
        return result;
    }
}