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

namespace NodeService.WebServer.Services.Queries
{
    public class CommonConfigBatchQueryQueueService : BackgroundService
    {
        private readonly BatchQueue<BatchQueueOperation<CommonConfigBatchQueueOperationParameters, ListQueryResult<object>>> _batchQueue;
        private readonly IServiceProvider _serviceProvider;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<CommonConfigBatchQueryQueueService> _logger;
        private readonly ExceptionCounter _exceptionCounter;
        private readonly ConcurrentDictionary<string, Delegate> _funcDict;

        public CommonConfigBatchQueryQueueService(
            ILogger<CommonConfigBatchQueryQueueService> logger,
            ExceptionCounter exceptionCounter,
            IServiceProvider serviceProvider,
            BatchQueue<BatchQueueOperation<CommonConfigBatchQueueOperationParameters, ListQueryResult<object>>> batchQueue,
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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var arrayPoolCollection in _batchQueue.ReceiveAllAsync(stoppingToken))
            {
                int count = arrayPoolCollection.CountNotDefault();
                if (count == 0)
                {
                    continue;
                }
                Stopwatch stopwatch = Stopwatch.StartNew();
                try
                {
                    foreach (var batchQueueOperationGroup in arrayPoolCollection.Where(static x => x != null)
                                                                                .OrderByDescending(x => x.Priority)
                                                                                .GroupBy(x => x.Argument))
                    {

                        try
                        {
                            var argument = batchQueueOperationGroup.Key;
                            _logger.LogInformation($"QueryParameters:{argument.QueryParameters},Type:{argument.Type},{batchQueueOperationGroup.Count()} requests");

                            ListQueryResult<object> result = default;
                            if (argument.QueryParameters != null)
                            {
                                var key = $"{argument.Type}-{nameof(CreateQueryByParameterLambda)}";
                                if (!_funcDict.TryGetValue(key, out var func))
                                {
                                    var expr = CreateQueryByParameterLambda(argument.Type);
                                    func = expr.Compile();
                                    _funcDict.TryAdd(key, func);
                                }
                                var task = ((Func<PaginationQueryParameters, Task<ListQueryResult<object>>>)func).Invoke(argument.QueryParameters);
                                result = await task;

                            }
                            else if (argument.Id != null)
                            {
                                var key = $"{argument.Type}-{nameof(CreateQueryByIdLambda)}";
                                if (!_funcDict.TryGetValue(key, out var func))
                                {
                                    var expr = CreateQueryByIdLambda(argument.Type);
                                    func = expr.Compile();
                                    _funcDict.TryAdd(key, func);
                                }
                                var task = ((Func<string, Task<ListQueryResult<object>>>)func).Invoke(argument.Id);
                                result = await task;
                            }

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
                catch (Exception ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }
                finally
                {
                    arrayPoolCollection.Dispose();
                    stopwatch.Stop();
                    _logger.LogInformation($"{count} requests,Ellapsed{stopwatch.Elapsed}");
                }
            }
        }

        Expression<Func<PaginationQueryParameters, Task<ListQueryResult<object?>>>> CreateQueryByParameterLambda(System.Type type)
        {
            var thisExpr = Expression.Constant(this);
            var queryParameters = Expression.Parameter(
                typeof(PaginationQueryParameters), "queryParameters");

            var callExpr = Expression.Call(thisExpr, nameof(QueryByQueryParametersAsync), [type], queryParameters);

            return Expression.Lambda<Func<PaginationQueryParameters, Task<ListQueryResult<object?>>>>(callExpr, queryParameters);
        }

        Expression<Func<string, Task<ListQueryResult<object?>>>> CreateQueryByIdLambda(System.Type type)
        {
            var thisExpr = Expression.Constant(this);
            var idParameter = Expression.Parameter(
                typeof(string), "id");

            var callExpr = Expression.Call(thisExpr, nameof(QueryByIdAsync), [type], idParameter);

            return Expression.Lambda<Func<string, Task<ListQueryResult<object?>>>>(callExpr, idParameter);
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
                    new CommonConfigSpecification<T>(queryParameters.Keywords),
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

        private async Task<ListQueryResult<object>> QueryByIdAsync<T>(string id)
    where T : ModelBase, IAggregateRoot
        {
            var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
            using var repo = repoFactory.CreateRepository();
            _logger.LogInformation($"{typeof(T).FullName}:{id}");
            var value = await repo.GetByIdAsync(id);
            ListQueryResult<object> queryResult = default;
            if (value != null)
            {
                queryResult = new ListQueryResult<object>(
                     1,
                     1,
                     1,
                     [value]);
            }

            return queryResult;
        }
    }
}
