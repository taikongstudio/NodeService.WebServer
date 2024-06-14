using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.Models;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Linq.Expressions;
using Ardalis.Result;

namespace NodeService.WebServer.Services.QueryOptimize
{
    public class CommonConfigBatchQueryQueueService : BackgroundService
    {
        private readonly BatchQueue<BatchQueueOperation<CommonConfigBatchQueryParameters, ListQueryResult<object>>> _batchQueue;
        private readonly IServiceProvider _serviceProvider;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<CommonConfigBatchQueryQueueService> _logger;
        private readonly ExceptionCounter _exceptionCounter;
        private readonly ConcurrentDictionary<string, Delegate> _funcDict;

        public CommonConfigBatchQueryQueueService(
            ILogger<CommonConfigBatchQueryQueueService> logger,
            ExceptionCounter exceptionCounter,
            IServiceProvider serviceProvider,
            BatchQueue<BatchQueueOperation<CommonConfigBatchQueryParameters, ListQueryResult<object>>> batchQueue,
            IMemoryCache memoryCache
            )
        {
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _batchQueue = batchQueue;
            _serviceProvider = serviceProvider;
            _memoryCache = memoryCache;
            _funcDict = new ConcurrentDictionary<string, Delegate>();
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            await foreach (var arrayPoolCollection in _batchQueue.ReceiveAllAsync(cancellationToken))
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                try
                {
                    await ProcessQueryByIdAsync(arrayPoolCollection);

                    await ProcessQueryByParameterAsync(arrayPoolCollection);

                }
                catch (Exception ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }
                finally
                {
                    stopwatch.Stop();
                    _logger.LogInformation($"{arrayPoolCollection.Count} requests,Ellapsed{stopwatch.Elapsed}");
                }
            }
        }

        private async Task ProcessQueryByParameterAsync(ArrayPoolCollection<BatchQueueOperation<CommonConfigBatchQueryParameters, ListQueryResult<object>>?> arrayPoolCollection)
        {
            foreach (var batchQueueOperationGroup in arrayPoolCollection.Where(static x => x != null && x.Argument.QueryParameters != null)
                                                                        .OrderByDescending(x => x.Priority)
                                                                        .GroupBy(x => x.Argument))
            {

                try
                {
                    var argument = batchQueueOperationGroup.Key;
                    _logger.LogInformation($"QueryParameters:{argument.QueryParameters},Type:{argument.Type},{batchQueueOperationGroup.Count()} requests");

                    ListQueryResult<object> result = default;
                    var funcKey = $"{argument.Type}-{nameof(CreateQueryByParameterLambdaExpression)}";
                    if (!_funcDict.TryGetValue(funcKey, out var func))
                    {
                        var expr = CreateQueryByParameterLambdaExpression(argument.Type);
                        func = expr.Compile();
                        _funcDict.TryAdd(funcKey, func);
                    }
                    var task = ((Func<PaginationQueryParameters, Task<ListQueryResult<object>>>)func).Invoke(argument.QueryParameters);
                    result = await task;
                    foreach (var operation in batchQueueOperationGroup)
                    {
                        operation.SetResult(result);
                    }
                }
                catch (Exception ex)
                {
                    foreach (var operation in batchQueueOperationGroup)
                    {
                        operation.SetException(ex);
                    }
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }

            }
        }

        private async Task ProcessQueryByIdAsync(ArrayPoolCollection<BatchQueueOperation<CommonConfigBatchQueryParameters, ListQueryResult<object>>?> arrayPoolCollection)
        {
            foreach (var batchQueueOperationGroup in arrayPoolCollection.Where(static x => x != null && x.Argument.Id != null)
                                                    .OrderByDescending(x => x.Priority)
                                                    .GroupBy(x => x.Argument.Type))
            {
                try
                {
                    var type = batchQueueOperationGroup.Key;
                    var idList = batchQueueOperationGroup.Select(static x => x.Argument.Id);
                    _logger.LogInformation($"{type}:{string.Join(",", idList)}");
                    if (idList == null || !idList.Any())
                    {
                        foreach (var op in batchQueueOperationGroup)
                        {
                            op.SetResult(default);
                        }
                    }
                    else
                    {
                        var funcKey = $"{type}-{nameof(CreateQueryByIdListLambdaExpression)}";
                        if (!_funcDict.TryGetValue(funcKey, out var func))
                        {
                            var expr = CreateQueryByIdListLambdaExpression(type);
                            func = expr.Compile();
                            _funcDict.TryAdd(funcKey, func);
                        }
                        var task = ((Func<IEnumerable<string>, Task<ListQueryResult<object>>>)func).Invoke(idList);
                        var queryResult = await task;
                        bool hasValue = queryResult.HasValue;
                        foreach (var op in batchQueueOperationGroup)
                        {
                            if (hasValue && TryFindItem(queryResult.Items, op.Argument.Id, out var result))
                            {
                                op.SetResult(new ListQueryResult<object>(1, 1, 1, [result]));

                            }
                            else
                            {
                                op.SetResult(default);
                            }
                        }
                    }

                }
                catch (Exception ex)
                {
                    foreach (var op in batchQueueOperationGroup)
                    {
                        op.SetException(ex);
                    }
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }

            }
        }

        private static bool TryFindItem(
            IEnumerable<object> items,
            string id, out ModelBase? result)
        {
            result = null;
            foreach (var item in items)
            {
                if (item is not ModelBase model)
                {
                    continue;
                }
                if (model.Id == id)
                {
                    result = model;
                    break;
                }
            }

            return result != null;
        }

        Expression<Func<PaginationQueryParameters, Task<ListQueryResult<object?>>>> CreateQueryByParameterLambdaExpression(Type type)
        {
            var thisExpr = Expression.Constant(this);
            var queryParameters = Expression.Parameter(
                typeof(PaginationQueryParameters), "queryParameters");

            var callExpr = Expression.Call(thisExpr, nameof(QueryByQueryParametersAsync), [type], queryParameters);

            return Expression.Lambda<Func<PaginationQueryParameters, Task<ListQueryResult<object?>>>>(callExpr, queryParameters);
        }

        Expression<Func<IEnumerable<string>, Task<ListQueryResult<object?>>>> CreateQueryByIdListLambdaExpression(Type type)
        {
            var thisExpr = Expression.Constant(this);
            var idListParameter = Expression.Parameter(
                typeof(IEnumerable<string>), "idList");

            var callExpr = Expression.Call(thisExpr, nameof(QueryByIdListAsync), [type], idListParameter);

            return Expression.Lambda<Func<IEnumerable<string>, Task<ListQueryResult<object?>>>>(callExpr, idListParameter);
        }

        private async Task<ListQueryResult<object>> QueryByQueryParametersAsync<T>(PaginationQueryParameters queryParameters)
            where T : ModelBase, IAggregateRoot
        {
            var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
            _logger.LogInformation($"{typeof(T).FullName}:{queryParameters}");
            ListQueryResult<T> listQueryResult = default;
            if (queryParameters.QueryStrategy == QueryStrategy.QueryPreferred)
            {
                using var repo = repoFactory.CreateRepository();
                listQueryResult = await repo.PaginationQueryAsync(
                    new CommonConfigSpecification<T>(queryParameters.Keywords, queryParameters.SortDescriptions),
                    queryParameters.PageSize,
                    queryParameters.PageIndex);
            }
            else if (queryParameters.QueryStrategy == QueryStrategy.CachePreferred)
            {
                var key = $"{typeof(T).FullName}:{queryParameters}";

                if (!_memoryCache.TryGetValue(key, out listQueryResult) || !listQueryResult.HasValue)
                {
                    using var repo = repoFactory.CreateRepository();
                    listQueryResult = await repo.PaginationQueryAsync(
                    new CommonConfigSpecification<T>(
                    queryParameters.Keywords,
                            queryParameters.SortDescriptions),
                        queryParameters.PageSize,
                        queryParameters.PageIndex);

                    if (listQueryResult.HasValue)
                        _memoryCache.Set(key, listQueryResult, TimeSpan.FromMinutes(1));
                }
            }
            ListQueryResult<object> queryResult = new ListQueryResult<object>(
                listQueryResult.TotalCount,
                listQueryResult.PageSize,
                listQueryResult.PageIndex,
                listQueryResult.Items.Select(x => x as object));
            return queryResult;
        }

        private async Task<ListQueryResult<object>> QueryByIdListAsync<T>(IEnumerable<string> idList)
    where T : ModelBase, IAggregateRoot
        {
            var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
            using var repo = repoFactory.CreateRepository();
            _logger.LogInformation($"{typeof(T).FullName}:{string.Join(",", idList)}");
            var result = await repo.ListAsync(new CommonConfigSpecification<T>(DataFilterCollection<string>.Includes(idList)));
            ListQueryResult<object> queryResult = default;
            if (result != null)
            {
                queryResult = new ListQueryResult<object>(
                     result.Count,
                     1,
                     result.Count,
                     result.Select(x => x as object));
            }

            return queryResult;
        }
    }
}
