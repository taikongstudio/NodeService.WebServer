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
    readonly ExceptionCounter _exceptionCounter;
    readonly BatchQueue<TaskLogUnit> _taskLogUnitBatchQueue;
    readonly WebServerCounter _webServerCounter;

    readonly IMemoryCache _memoryCache;
    readonly ILogger<TaskLogPersistenceService> _logger;
    readonly IServiceProvider _serviceProvider;
    readonly ApplicationRepositoryFactory<TaskLogModel> _taskLogRepoFactory;
    readonly ApplicationRepositoryFactory<TaskExecutionInstanceModel> _taskExecutionInstanceFactory;
    readonly ConcurrentDictionary<int, TaskLogHandler> _taskLogHandlers;
    readonly Timer _timer;
    IEnumerable<int> _keys = [];

    public TaskLogPersistenceService(
        IServiceProvider serviceProvider,
        ILogger<TaskLogPersistenceService> logger,
        ApplicationRepositoryFactory<TaskLogModel> taskLogRepoFactory,
        ApplicationRepositoryFactory<TaskExecutionInstanceModel> taskExecutionInstanceFactory,
        BatchQueue<TaskLogUnit> taskLogUnitBatchQueue,
        ExceptionCounter exceptionCounter,
        WebServerCounter webServerCounter,
        IMemoryCache memoryCache)
    {
        _logger = logger;
        _taskLogRepoFactory = taskLogRepoFactory;
        _taskExecutionInstanceFactory = taskExecutionInstanceFactory;
        _exceptionCounter = exceptionCounter;
        _taskLogUnitBatchQueue = taskLogUnitBatchQueue;
        _webServerCounter = webServerCounter;
        _memoryCache = memoryCache;
        _taskLogHandlers = new ConcurrentDictionary<int, TaskLogHandler>();
        _serviceProvider = serviceProvider;
        _timer = new Timer(OnTimer);
        _timer.Change(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
    }

    private async void OnTimer(object? state)
    {
        await _taskLogUnitBatchQueue.SendAsync(new TaskLogUnit() { Id = null, LogEntries = [] });
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(ProcessExpiredTaskLogsAsync(cancellationToken), ProcessTaskLogUnitAsync(cancellationToken));
    }

    async Task ProcessExpiredTaskLogsAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(TimeSpan.FromMinutes(10), cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var taskLogRepo = _taskLogRepoFactory.CreateRepository();
                var applicationDbContext = taskLogRepo.DbContext as ApplicationDbContext;
                await foreach (var taskExecutionInstance in QueryExpiredTaskExecutionInstanceAsync(cancellationToken))
                {
                    await DeleteTaskLogAsync(taskExecutionInstance, applicationDbContext, cancellationToken);
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
            finally
            {
                await Task.Delay(TimeSpan.FromHours(6), cancellationToken);
            }
        }

        async IAsyncEnumerable<TaskExecutionInstanceModel> QueryExpiredTaskExecutionInstanceAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using var repoFactory = _taskExecutionInstanceFactory.CreateRepository();
            int pageIndex = 1;
            int pageSize = 100;
            while (!cancellationToken.IsCancellationRequested)
            {
                var queryResult = await repoFactory.PaginationQueryAsync(
                     new TaskExecutionInstanceSpecification(DateTime.UtcNow.AddDays(-14)),
                     new PaginationInfo(pageIndex, pageSize),
                     cancellationToken);
                if (!queryResult.HasValue)
                {
                    break;
                }
                if (queryResult.Items.Count() < pageSize)
                {
                    break;
                }
                foreach (var item in queryResult.Items)
                {
                    yield return item;
                }
                pageIndex++;
            }
            yield break;
        }

        async Task DeleteTaskLogAsync(TaskExecutionInstanceModel taskExecutionInstance, ApplicationDbContext applicationDbContext, CancellationToken cancellationToken = default)
        {
            int deleteCount = await applicationDbContext.TaskLogDbSet.Where(x => x.Id.StartsWith(taskExecutionInstance.Id)).ExecuteDeleteAsync(cancellationToken);
            if (deleteCount > 0)
            {
                await applicationDbContext.TaskExecutionInstanceDbSet.Where(x => x.Id == taskExecutionInstance.Id)
                    .ExecuteUpdateAsync(x => x.SetProperty(x => x.LogEntriesSaveCount, 0),
                    cancellationToken);
            }
        }
    }

    async Task ProcessTaskLogUnitAsync(CancellationToken cancellationToken = default)
    {
        try
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
                    _logger.LogInformation("Begin");

                    if (Debugger.IsAttached)
                    {
                        foreach (var item in array.GroupBy(TaskLogUnitGroupFunc))
                        {
                            await RunTaskLogHandlerAsync(item, cancellationToken);
                        }
                    }
                    else
                    {
                        await Parallel.ForEachAsync(
                                array.GroupBy(TaskLogUnitGroupFunc).Where(static x => x.Key != -1).ToArray(),
                                new ParallelOptions()
                                {
                                    CancellationToken = cancellationToken,
                                    MaxDegreeOfParallelism = 4,
                                },
                                RunTaskLogHandlerAsync);
                    }


                    _logger.LogInformation("End");
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
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }

    }

    void Stat()
    {
        _webServerCounter.TaskLogPageCount.Value = _taskLogHandlers.Values.Sum(x => x.TotalCreatedPageCount);
        _webServerCounter.TaskLogUnitConsumeCount.Value = _taskLogHandlers.Values.Sum(x => x.TotalGroupConsumeCount);
        _webServerCounter.TaskLogEntriesSavedCount.Value = _taskLogHandlers.Values.Sum(x => x.TotalLogEntriesSavedCount);
        if (_taskLogHandlers.Values.Count > 0)
        {
            _webServerCounter.TaskLogUnitSaveMaxTimeSpan.Value = _taskLogHandlers.Values.Max(x => x.TotalSaveMaxTimeSpan);
            _webServerCounter.TaskLogUnitQueryTimeSpan.Value = _taskLogHandlers.Values.Max(x => x.TotalQueryTimeSpan);
            _webServerCounter.TaskLogUnitSaveTimeSpan.Value = _taskLogHandlers.Values.Max(x => x.TotalSaveTimeSpan);
        }
        _webServerCounter.TaskLogUnitAvailableCount.Value = _taskLogUnitBatchQueue.AvailableCount;
        _webServerCounter.TaskLogPageDetachedCount.Value = _taskLogHandlers.Values.Sum(x => x.TotalCreatedPageCount);
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
        if (unit.Id == null) return 0;
        Math.DivRem(unit.Id[0], 10, out var result);
        return result;
    }
}