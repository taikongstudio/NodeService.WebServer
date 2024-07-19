using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Services.Tasks;

public class TaskLogPersistenceService : BackgroundService
{
    readonly ExceptionCounter _exceptionCounter;
    readonly BatchQueue<AsyncOperation<TaskLogUnit[]>> _taskLogUnitBatchQueue;
    readonly WebServerCounter _webServerCounter;

    readonly IMemoryCache _memoryCache;
    readonly ILogger<TaskLogPersistenceService> _logger;
    readonly IServiceProvider _serviceProvider;
    readonly ApplicationRepositoryFactory<TaskLogModel> _taskLogRepoFactory;
    readonly ApplicationRepositoryFactory<TaskExecutionInstanceModel> _taskExecutionInstanceFactory;
    readonly ConcurrentDictionary<int, TaskLogStorageHandlerBase> _taskLogHandlers;
    IEnumerable<int> _keys = [];

    public TaskLogPersistenceService(
        IServiceProvider serviceProvider,
        ILogger<TaskLogPersistenceService> logger,
        ApplicationRepositoryFactory<TaskLogModel> taskLogRepoFactory,
        ApplicationRepositoryFactory<TaskExecutionInstanceModel> taskExecutionInstanceFactory,
        [FromKeyedServices(nameof(TaskLogPersistenceService))]BatchQueue<AsyncOperation<TaskLogUnit[]>> taskLogUnitBatchQueue,
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
        _taskLogHandlers = new ConcurrentDictionary<int, TaskLogStorageHandlerBase>();
        _serviceProvider = serviceProvider;
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
                await foreach (var taskExecutionInstance in QueryExpiredTaskExecutionInstanceAsync(cancellationToken))
                {
                    await DeleteTaskLogAsync(taskExecutionInstance, cancellationToken);
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
            await using var repoFactory = await _taskExecutionInstanceFactory.CreateRepositoryAsync();
            int pageIndex = 1;
            int pageSize = 100;
            while (!cancellationToken.IsCancellationRequested)
            {
                var queryResult = await repoFactory.PaginationQueryAsync(
                     new TaskExecutionInstanceListSpecification(DateTime.UtcNow.AddDays(-14)),
                     new PaginationInfo(pageIndex, pageSize),
                     cancellationToken);
                if (!queryResult.HasValue)
                {
                    break;
                }
                foreach (var item in queryResult.Items)
                {
                    yield return item;
                }
                if (queryResult.Items.Count() < pageSize)
                {
                    break;
                }
                pageIndex++;
            }
            yield break;
        }

        async Task DeleteTaskLogAsync(TaskExecutionInstanceModel taskExecutionInstance, CancellationToken cancellationToken = default)
        {
            //await using var taskLogRepo = await _taskLogRepoFactory.CreateRepositoryAsync(cancellationToken);
            //var applicationDbContext = taskLogRepo.DbContext as ApplicationDbContext;
            //int deleteCount = await applicationDbContext.TaskLogStorageDbSet.Where(x => x.Id.StartsWith(taskExecutionInstance.Id)).ExecuteDeleteAsync(cancellationToken);
            //if (deleteCount > 0)
            //{
            //    await applicationDbContext.TaskExecutionInstanceDbSet.Where(x => x.Id == taskExecutionInstance.Id)
            //        .ExecuteUpdateAsync(x => x.SetProperty(x => x.LogEntriesSaveCount, 0),
            //        cancellationToken);
            //}
            await Task.CompletedTask;
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
                    _webServerCounter.TaskLogUnitQueueCount.Value = _taskLogUnitBatchQueue.QueueCount;
                    if (array == null)
                    {
                        continue;
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
                                    MaxDegreeOfParallelism = 2,
                                },
                                RunTaskLogHandlerAsync);
                    }


                    _logger.LogInformation("End");
                    _keys = _taskLogHandlers.Keys;
                    _webServerCounter.TaskLogHandlerCount.Value = _taskLogHandlers.Count;
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
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }

    }

    async ValueTask RunTaskLogHandlerAsync(IGrouping<int, AsyncOperation<TaskLogUnit[]>> group,
        CancellationToken cancellationToken)
    {
        var key = group.Key;
        var handler = _taskLogHandlers.GetOrAdd(key, CreateTaskLogHandlerFactory);
        foreach (var op in group)
        {
            await handler.ProcessAsync(op.Argument, cancellationToken);
            op.TrySetResult();
        }
    }

    TaskLogStorageHandlerBase CreateTaskLogHandlerFactory(int id)
    {
        var taskLogHandler = ActivatorUtilities.CreateInstance<MongodbLogStorageHandler>(_serviceProvider);
        return taskLogHandler;
    }

    int TaskLogUnitGroupFunc(AsyncOperation<TaskLogUnit[]> asyncOperation)
    {
        Math.DivRem(asyncOperation.Argument[0].Id[0], 10, out var result);
        return result;
    }
}