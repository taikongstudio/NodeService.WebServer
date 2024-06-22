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
using System.Collections.Immutable;
using System.Collections;
using OneOf;

namespace NodeService.WebServer.Services.QueryOptimize;

public record struct CommonConfigQueryQueueServiceParameters
{
    public CommonConfigQueryQueueServiceParameters(Type type, PaginationQueryParameters queryParameters)
    {
        Parameters = queryParameters;
        Type = type;
    }

    public CommonConfigQueryQueueServiceParameters(Type type, string id)
    {
        Parameters = id;
        Type = type;
    }

    public CommonConfigQueryQueueServiceParameters(Type type, JsonBasedDataModel entity)
    {
        Parameters = entity;
        Type = type;
    }

    public OneOf<PaginationQueryParameters, string, JsonBasedDataModel> Parameters { get; private set; }

    public Type Type { get; private set; }
}

public record struct CommonConfigQueryQueueServiceResult
{
    public CommonConfigQueryQueueServiceResult(ListQueryResult<object> result)
    {
        Value = result;
    }

    public CommonConfigQueryQueueServiceResult(ConfigurationSaveChangesResult  saveChangesResult)
    {
        Value = saveChangesResult;
    }

    public OneOf<ListQueryResult<object>, ConfigurationSaveChangesResult> Value { get; private set; }
}


public class CommonConfigBatchQueryQueueService : BackgroundService
{
    private readonly BatchQueue<BatchQueueOperation<CommonConfigQueryQueueServiceParameters, CommonConfigQueryQueueServiceResult>>
        _batchQueue;

    private readonly IServiceProvider _serviceProvider;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<CommonConfigBatchQueryQueueService> _logger;
    private readonly ExceptionCounter _exceptionCounter;
    private readonly ConcurrentDictionary<string, Delegate> _funcDict;

    public CommonConfigBatchQueryQueueService(
        ILogger<CommonConfigBatchQueryQueueService> logger,
        ExceptionCounter exceptionCounter,
        IServiceProvider serviceProvider,
        BatchQueue<BatchQueueOperation<CommonConfigQueryQueueServiceParameters, CommonConfigQueryQueueServiceResult>> batchQueue,
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
        await foreach (var array in _batchQueue.ReceiveAllAsync(cancellationToken))
        {
            if (array == null)
            {
                continue;
            }
            var stopwatch = Stopwatch.StartNew();
            try
            {
                foreach (var operationGroup in GroupAdjacent(array))
                {
                    if (operationGroup.IsDefaultOrEmpty)
                    {
                        continue;
                    }
                    var kind = operationGroup[0].Kind;
                    switch (kind)
                    {
                        case BatchQueueOperationKind.None:
                            break;
                        case BatchQueueOperationKind.AddOrUpdate:
                            await ProcessAddOrUpdateAsync(operationGroup);
                            break;
                        case BatchQueueOperationKind.Delete:
                            await ProcessDeleteAsync(operationGroup);
                            break;
                        case BatchQueueOperationKind.Query:
                            await ProcessQueryByIdAsync(operationGroup);
                            await ProcessQueryByParameterAsync(operationGroup);
                            break;
                        default:
                            break;
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
                stopwatch.Stop();
                _logger.LogInformation($"{array.Length} requests,Ellapsed:{stopwatch.Elapsed}");
            }
        }
    }

    static IEnumerable<ImmutableArray<BatchQueueOperation<CommonConfigQueryQueueServiceParameters, CommonConfigQueryQueueServiceResult>>>
        GroupAdjacent(BatchQueueOperation<CommonConfigQueryQueueServiceParameters, CommonConfigQueryQueueServiceResult>[] array)
    {
        var eble = array.AsEnumerable();
        var etor = eble.GetEnumerator();
        if (etor.MoveNext())
        {
            var immutableArray = ImmutableArray.Create(etor.Current);
            var pred = etor.Current;
            while (etor.MoveNext())
            {
                if (Predicate(pred, etor.Current))
                {
                    immutableArray = immutableArray.Add(etor.Current);
                }
                else
                {
                    yield return immutableArray;
                    immutableArray = [etor.Current];
                }
                pred = etor.Current;
            }
            yield return immutableArray;
        }

        static bool Predicate(BatchQueueOperation<CommonConfigQueryQueueServiceParameters, CommonConfigQueryQueueServiceResult> left,
           BatchQueueOperation<CommonConfigQueryQueueServiceParameters, CommonConfigQueryQueueServiceResult> right)
        {
            return left.Kind == right.Kind && left.Argument.Parameters.Index == right.Argument.Parameters.Index;
        }
    }

    async Task ProcessDeleteAsync(
IEnumerable<BatchQueueOperation<CommonConfigQueryQueueServiceParameters, CommonConfigQueryQueueServiceResult>> array)
    {
        foreach (var operationGroup in array
                     .OrderByDescending(x => x.Priority)
                     .GroupBy(x => x.Argument.Type))
            try
            {
                var type = operationGroup.Key;

                foreach (var operation in operationGroup)
                {
                    var funcKey = $"{type}-{nameof(CreateDeleteLambdaExpression)}";
                    if (!_funcDict.TryGetValue(funcKey, out var func))
                    {
                        var expr = CreateDeleteLambdaExpression(type);
                        func = expr.Compile();
                        _funcDict.TryAdd(funcKey, func);
                    }

                    var task = ((Func<object, ValueTask<ConfigurationSaveChangesResult>>)func).Invoke(operation.Argument.Parameters.AsT2);
                    var saveChangesResult = await task;
                    operation.SetResult(new CommonConfigQueryQueueServiceResult(saveChangesResult));
                }

            }
            catch (Exception ex)
            {
                foreach (var op in operationGroup) op.SetException(ex);
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
    }

    async Task ProcessAddOrUpdateAsync(
    IEnumerable<BatchQueueOperation<CommonConfigQueryQueueServiceParameters, CommonConfigQueryQueueServiceResult>> array)
    {
        foreach (var operationGroup in array
                     .OrderByDescending(x => x.Priority)
                     .GroupBy(x => x.Argument.Type))
            try
            {
                var type = operationGroup.Key;

                foreach (var operation in operationGroup)
                {
                    var funcKey = $"{type}-{nameof(CreateAddOrUpdateLambdaExpression)}";
                    if (!_funcDict.TryGetValue(funcKey, out var func))
                    {
                        var expr = CreateAddOrUpdateLambdaExpression(type);
                        func = expr.Compile();
                        _funcDict.TryAdd(funcKey, func);
                    }

                    var task = ((Func<object, ValueTask<ConfigurationSaveChangesResult>>)func).Invoke(operation.Argument.Parameters.AsT2);
                    var saveChangesResult = await task;
                    operation.SetResult(new CommonConfigQueryQueueServiceResult(saveChangesResult));
                }

            }
            catch (Exception ex)
            {
                foreach (var op in operationGroup) op.SetException(ex);
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
    }


    async Task ProcessQueryByParameterAsync(
      IEnumerable<BatchQueueOperation<CommonConfigQueryQueueServiceParameters, CommonConfigQueryQueueServiceResult>> array)
    {
        foreach (var batchQueueOperationGroup in array
                     .Where(static x => x != null && x.Argument.Parameters.Index==0)
                     .OrderByDescending(static x => x.Priority)
                     .GroupBy(static x => x.Argument))
        {
            try
            {
                var argument = batchQueueOperationGroup.Key;
                _logger.LogInformation(
                    $"QueryParameters:{argument.Parameters.AsT0},Type:{argument.Type},{batchQueueOperationGroup.Count()} requests");

                ListQueryResult<object> result = default;
                var funcKey = $"{argument.Type}-{nameof(CreateQueryByParameterLambdaExpression)}";
                if (!_funcDict.TryGetValue(funcKey, out var func))
                {
                    var expr = CreateQueryByParameterLambdaExpression(argument.Type);
                    func = expr.Compile();
                    _funcDict.TryAdd(funcKey, func);
                }

                var task = ((Func<PaginationQueryParameters, ValueTask<ListQueryResult<object>>>)func).Invoke(argument.Parameters.AsT0);
                result = await task;
                foreach (var operation in batchQueueOperationGroup) operation.SetResult(new CommonConfigQueryQueueServiceResult(result));
            }
            catch (Exception ex)
            {
                foreach (var operation in batchQueueOperationGroup) operation.SetException(ex);
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
        }

    }

     async Task ProcessQueryByIdAsync(IEnumerable<BatchQueueOperation<CommonConfigQueryQueueServiceParameters, CommonConfigQueryQueueServiceResult>> array)
    {
        foreach (var batchQueueOperationGroup in array
                     .Where(static x => x != null && x.Argument.Parameters.Index==1)
                     .OrderByDescending(x => x.Priority)
                     .GroupBy(x => x.Argument.Type))
            try
            {
                var type = batchQueueOperationGroup.Key;
                var idList = batchQueueOperationGroup.Select(static x => x.Argument.Parameters.AsT1);
                _logger.LogInformation($"{type}:{string.Join(",", idList)}");
                if (idList == null || !idList.Any())
                {
                    foreach (var op in batchQueueOperationGroup) op.SetResult(default);
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

                    var task = ((Func<IEnumerable<string>, ValueTask<ListQueryResult<object>>>)func).Invoke(idList);
                    var queryResult = await task;
                    var hasValue = queryResult.HasValue;
                    foreach (var op in batchQueueOperationGroup)
                        if (hasValue && TryFindItem(queryResult.Items, op.Argument.Parameters.AsT1, out var result))
                            op.SetResult(new CommonConfigQueryQueueServiceResult(new ListQueryResult<object>(1, 1, 1, [result])));
                        else
                            op.SetResult(default);
                }
            }
            catch (Exception ex)
            {
                foreach (var op in batchQueueOperationGroup) op.SetException(ex);
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
    }

    private static bool TryFindItem(
        IEnumerable<object> items,
        string id, out ModelBase? result)
    {
        result = null;
        foreach (var item in items)
        {
            if (item is not ModelBase model) continue;
            if (model.Id == id)
            {
                result = model;
                break;
            }
        }

        return result != null;
    }

     Expression<Func<PaginationQueryParameters, ValueTask<ListQueryResult<object?>>>>
        CreateQueryByParameterLambdaExpression(Type type)
    {
        var thisExpr = Expression.Constant(this);
        var queryParameters = Expression.Parameter(
            typeof(PaginationQueryParameters), "queryParameters");

        var callExpr = Expression.Call(thisExpr, nameof(QueryByQueryParametersAsync), [type], queryParameters);

        return Expression.Lambda<Func<PaginationQueryParameters, ValueTask<ListQueryResult<object?>>>>(callExpr,
            queryParameters);
    }

     Expression<Func<IEnumerable<string>, ValueTask<ListQueryResult<object?>>>>
        CreateQueryByIdListLambdaExpression(Type type)
    {
        var thisExpr = Expression.Constant(this);
        var idListParameter = Expression.Parameter(
            typeof(IEnumerable<string>), "idList");

        var callExpr = Expression.Call(thisExpr, nameof(QueryByIdListAsync), [type], idListParameter);

        return Expression.Lambda<Func<IEnumerable<string>, ValueTask<ListQueryResult<object?>>>>(callExpr, idListParameter);
    }

    Expression<Func<object, ValueTask<ConfigurationSaveChangesResult>>>
    CreateAddOrUpdateLambdaExpression(Type type)
    {
        var thisExpr = Expression.Constant(this);
        var entityParameters = Expression.Parameter( typeof(object), "entity");

        var callExpr = Expression.Call(thisExpr, nameof(AddOrUpdateAsync), [type], entityParameters);

        return Expression.Lambda<Func<object, ValueTask<ConfigurationSaveChangesResult>>>(callExpr, entityParameters);
    }

    Expression<Func<object, ValueTask<ConfigurationSaveChangesResult>>> CreateDeleteLambdaExpression(Type type)
    {
        var thisExpr = Expression.Constant(this);
        var entityParameters = Expression.Parameter(typeof(object), "entity");

        var callExpr = Expression.Call(thisExpr, nameof(DeleteAsync), [type], entityParameters);

        return Expression.Lambda<Func<object, ValueTask<ConfigurationSaveChangesResult>>>(callExpr, entityParameters);
    }

    async ValueTask<ListQueryResult<object>> QueryByQueryParametersAsync<T>(
        PaginationQueryParameters queryParameters)
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

        ListQueryResult<object> queryResult = new(
            listQueryResult.TotalCount,
            listQueryResult.PageSize,
            listQueryResult.PageIndex,
            listQueryResult.Items.Select(x => x as object));
        return queryResult;
    }

    async ValueTask<ListQueryResult<object>> QueryByIdListAsync<T>(IEnumerable<string> idList)
        where T : ModelBase, IAggregateRoot
    {
        var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
        using var repo = repoFactory.CreateRepository();
        _logger.LogInformation($"{typeof(T).FullName}:{string.Join(",", idList)}");
        var result = await repo.ListAsync(new CommonConfigSpecification<T>(DataFilterCollection<string>.Includes(idList)));
        ListQueryResult<object> queryResult = default;
        if (result != null)
            queryResult = new ListQueryResult<object>(
                result.Count,
                1,
                result.Count,
                result.Select(x => x as object));

        return queryResult;
    }

    async ValueTask<ConfigurationSaveChangesResult> AddOrUpdateAsync<T>(object entityObject)
                where T : JsonBasedDataModel
    {
        var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
        using var repo = repoFactory.CreateRepository();
        var entity = entityObject as T;
        var entityFromDb = await repo.GetByIdAsync(entity.Id);
        var type = ConfigurationChangedType.None;
        var changesCount = 0;
        if (entityFromDb == null)
        {
            await repo.AddAsync(entity);
            changesCount = 1;
            entityFromDb = entity;
            type = ConfigurationChangedType.Add;
        }
        else
        {
            entityFromDb.With(entity);
           changesCount= await repo.SaveChangesAsync();
            type = ConfigurationChangedType.Update;
        }
        return new ConfigurationSaveChangesResult()
        {
            Type = type,
            ChangesCount = changesCount,
            Entity = entityFromDb
        };
    }

    async ValueTask<ConfigurationSaveChangesResult> DeleteAsync<T>(object entityObject)
            where T : JsonBasedDataModel
    {
        var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
        using var repo = repoFactory.CreateRepository();
        var entity = entityObject as T;
        await repo.DeleteAsync(entity);
        var type = ConfigurationChangedType.Delete;
        var changesCount = repo.LastChangesCount;
        return new ConfigurationSaveChangesResult()
        {
            Type = type,
            ChangesCount = changesCount,
        };
    }
}