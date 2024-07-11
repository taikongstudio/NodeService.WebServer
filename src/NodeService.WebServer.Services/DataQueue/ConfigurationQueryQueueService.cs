using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.Models;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;
using OneOf;
using System.Collections.Immutable;
using System.Configuration;
using System.Linq.Expressions;

namespace NodeService.WebServer.Services.DataQueue;



public record struct ConfigurationPaginationQueryParameters
{
    public ConfigurationPaginationQueryParameters(PaginationQueryParameters parameters)
    {
        Parameters = parameters;
    }

    public PaginationQueryParameters Parameters { get; private set; }
}

public record struct ConfigurationVersionPaginationQueryParameters
{
    public ConfigurationVersionPaginationQueryParameters(PaginationQueryParameters parameters)
    {
        Parameters = parameters;
    }

    public PaginationQueryParameters Parameters { get; private set; }
}

public record struct ConfigurationAddUpdateDeleteParameters
{
    public ConfigurationAddUpdateDeleteParameters(JsonRecordBase value)
    {
        Value = value;
    }

    public JsonRecordBase Value { get; private set; }
}

public record struct ConfigurationVersionDeleteParameters
{
    public ConfigurationVersionDeleteParameters(ConfigurationVersionRecordModel value)
    {
        Value = value;
    }

    public ConfigurationVersionRecordModel Value { get; private set; }
}

public record struct ConfigurationIdentityListQueryParameters
{
    public ConfigurationIdentityListQueryParameters(List<string> idList)
    {
        IdList = idList;
    }

    public List<string> IdList { get; private set; }
}

public record struct ConfigurationVersionIdentityQueryParameters
{
    public ConfigurationVersionIdentityQueryParameters(string id)
    {
        Id = id;
    }

    public string Id { get; private set; }
}


public record struct ConfigurationVersionSwitchParameters
{
    public ConfigurationVersionSwitchParameters(string configurationId, int targetVersion)
    {
        ConfigurationId = configurationId;
        TargetVersion = targetVersion;
    }

    public string ConfigurationId { get; set; }
    public int TargetVersion { get; set; }
}

public record struct ConfigurationQueryQueueServiceParameters
{
    public ConfigurationQueryQueueServiceParameters(Type type, ConfigurationVersionPaginationQueryParameters queryParameters)
    {
        Parameters = queryParameters;
        Type = type;
    }

    public ConfigurationQueryQueueServiceParameters(Type type, ConfigurationPaginationQueryParameters queryParameters)
    {
        Parameters = queryParameters;
        Type = type;
    }

    public ConfigurationQueryQueueServiceParameters(Type type, ConfigurationIdentityListQueryParameters queryParameters)
    {
        Parameters = queryParameters;
        Type = type;
    }


    public ConfigurationQueryQueueServiceParameters(Type type, ConfigurationAddUpdateDeleteParameters parameters)
    {
        Parameters = parameters;
        Type = type;
    }

    public ConfigurationQueryQueueServiceParameters(Type type, ConfigurationVersionSwitchParameters parameters)
    {
        Parameters = parameters;
        Type = type;
    }

    public ConfigurationQueryQueueServiceParameters(Type type, ConfigurationVersionDeleteParameters parameters)
    {
        Parameters = parameters;
        Type = type;
    }

    public OneOf<
        ConfigurationPaginationQueryParameters,
        ConfigurationVersionPaginationQueryParameters,
        ConfigurationIdentityListQueryParameters,
        ConfigurationVersionIdentityQueryParameters,
        ConfigurationAddUpdateDeleteParameters,
        ConfigurationVersionSwitchParameters,
        ConfigurationVersionDeleteParameters> Parameters
    { get; private set; }

    public Type Type { get; private set; }
}


public record struct ConfigurationQueryQueueServiceResult
{
    public ConfigurationQueryQueueServiceResult(ListQueryResult<object> result)
    {
        Value = result;
    }

    public ConfigurationQueryQueueServiceResult(ConfigurationSaveChangesResult saveChangesResult)
    {
        Value = saveChangesResult;
    }

    public ConfigurationQueryQueueServiceResult(ConfigurationVersionSaveChangesResult saveChangesResult)
    {
        Value = saveChangesResult;
    }

    public OneOf<ListQueryResult<object>, ConfigurationSaveChangesResult, ConfigurationVersionSaveChangesResult> Value { get; private set; }
}


public class ConfigurationQueryQueueService : BackgroundService
{
    readonly BatchQueue<BatchQueueOperation<ConfigurationQueryQueueServiceParameters, ConfigurationQueryQueueServiceResult>> _batchQueue;

    readonly IServiceProvider _serviceProvider;
    readonly IMemoryCache _memoryCache;
    readonly ApplicationRepositoryFactory<ConfigurationVersionRecordModel> _configVersionRepoFactory;
    readonly ILogger<ConfigurationQueryQueueService> _logger;
    readonly ExceptionCounter _exceptionCounter;
    readonly ConcurrentDictionary<string, Delegate> _funcDict;

    public ConfigurationQueryQueueService(
        ILogger<ConfigurationQueryQueueService> logger,
        ExceptionCounter exceptionCounter,
        IServiceProvider serviceProvider,
        BatchQueue<BatchQueueOperation<ConfigurationQueryQueueServiceParameters, ConfigurationQueryQueueServiceResult>> batchQueue,
        IMemoryCache memoryCache,
        ApplicationRepositoryFactory<ConfigurationVersionRecordModel> configVersionRepoFactory
    )
    {
        _logger = logger;
        _exceptionCounter = exceptionCounter;
        _batchQueue = batchQueue;
        _serviceProvider = serviceProvider;
        _memoryCache = memoryCache;
        _configVersionRepoFactory = configVersionRepoFactory;
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
                            await ProcessAddOrUpdateConfigurationAsync(operationGroup);
                            await ProcessSwitchConfigurationVersionAsync(operationGroup);
                            break;
                        case BatchQueueOperationKind.Delete:
                            await ProcessDeleteConfigurationAsync(operationGroup);
                            await ProcessDeleteConfigurationVersionAsync(operationGroup);
                            break;
                        case BatchQueueOperationKind.Query:
                            await ProcessQueryConfigurationByIdAsync(operationGroup);
                            await ProcessQueryConfigurationByParameterAsync(operationGroup);
                            await ProcessQueryConfigurationVersionByParameterAsync(operationGroup);
                            break;
                        default:
                            break;
                    }
                }

            }
            catch (Exception ex)
            {
                foreach (var operation in array)
                {
                    operation.SetException(ex);
                }
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

    async Task ProcessDeleteConfigurationVersionAsync(ImmutableArray<BatchQueueOperation<ConfigurationQueryQueueServiceParameters, ConfigurationQueryQueueServiceResult>> array)
    {
        foreach (var batchQueueOperationGroup in array
             .Where(static x => x != null && x.Argument.Parameters.Index == 6)
             .OrderByDescending(static x => x.Priority)
             .GroupBy(static x => x.Argument))
        {
            try
            {
                var argument = batchQueueOperationGroup.Key;
                _logger.LogInformation(
                    $"QueryParameters:{argument.Parameters.AsT6},Type:{argument.Type},{batchQueueOperationGroup.Count()} requests");

                var funcKey = $"{argument.Type}-{nameof(CreateConfigurationVersionDeleteLambdaExpression)}";
                if (!_funcDict.TryGetValue(funcKey, out var func))
                {
                    var expr = CreateConfigurationVersionDeleteLambdaExpression(argument.Type);
                    func = expr.Compile();
                    _funcDict.TryAdd(funcKey, func);
                }

                var task = ((Func<ConfigurationVersionDeleteParameters, ValueTask<ConfigurationVersionSaveChangesResult>>)func).Invoke(argument.Parameters.AsT6);
                var result = await task;
                foreach (var operation in batchQueueOperationGroup)
                {
                    operation.SetResult(new ConfigurationQueryQueueServiceResult(result));
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

    async Task ProcessSwitchConfigurationVersionAsync(ImmutableArray<BatchQueueOperation<ConfigurationQueryQueueServiceParameters, ConfigurationQueryQueueServiceResult>> array)
    {
        foreach (var batchQueueOperationGroup in array
                     .Where(static x => x != null && x.Argument.Parameters.Index == 5)
                     .OrderByDescending(static x => x.Priority)
                     .GroupBy(static x => x.Argument))
        {
            try
            {
                var argument = batchQueueOperationGroup.Key;
                _logger.LogInformation(
                    $"QueryParameters:{argument.Parameters.AsT5},Type:{argument.Type},{batchQueueOperationGroup.Count()} requests");

                var funcKey = $"{argument.Type}-{nameof(CreateConfigurationVersionSwitchLambdaExpression)}";
                if (!_funcDict.TryGetValue(funcKey, out var func))
                {
                    var expr = CreateConfigurationVersionSwitchLambdaExpression(argument.Type);
                    func = expr.Compile();
                    _funcDict.TryAdd(funcKey, func);
                }

                var task = ((Func<ConfigurationVersionSwitchParameters, ValueTask<ConfigurationSaveChangesResult>>)func).Invoke(argument.Parameters.AsT5);
                var result = await task;
                foreach (var operation in batchQueueOperationGroup)
                {
                    operation.SetResult(new ConfigurationQueryQueueServiceResult(result));
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

    async Task ProcessQueryConfigurationVersionByParameterAsync(ImmutableArray<BatchQueueOperation<ConfigurationQueryQueueServiceParameters, ConfigurationQueryQueueServiceResult>> array)
    {
        foreach (var batchQueueOperationGroup in array
                     .Where(static x => x != null && x.Argument.Parameters.Index == 1)
                     .OrderByDescending(static x => x.Priority)
                     .GroupBy(static x => x.Argument))
        {
            try
            {
                var argument = batchQueueOperationGroup.Key;
                _logger.LogInformation(
                    $"QueryParameters:{argument.Parameters.AsT1},Type:{argument.Type},{batchQueueOperationGroup.Count()} requests");

                ListQueryResult<object> result = default;
                var funcKey = $"{argument.Type}-{nameof(CreateQueryConfigurationVersionByParameterLambdaExpression)}";
                if (!_funcDict.TryGetValue(funcKey, out var func))
                {
                    var expr = CreateQueryConfigurationVersionByParameterLambdaExpression(argument.Type);
                    func = expr.Compile();
                    _funcDict.TryAdd(funcKey, func);
                }

                var task = ((Func<PaginationQueryParameters, ValueTask<ListQueryResult<object>>>)func).Invoke(argument.Parameters.AsT1.Parameters);
                result = await task;
                foreach (var operation in batchQueueOperationGroup)
                {
                    operation.SetResult(new ConfigurationQueryQueueServiceResult(result));
                }
            }
            catch (Exception ex)
            {
                foreach (var operation in batchQueueOperationGroup) operation.SetException(ex);
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
        }
    }

    static IEnumerable<ImmutableArray<BatchQueueOperation<ConfigurationQueryQueueServiceParameters, ConfigurationQueryQueueServiceResult>>>
        GroupAdjacent(BatchQueueOperation<ConfigurationQueryQueueServiceParameters, ConfigurationQueryQueueServiceResult>[] array)
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

        static bool Predicate(BatchQueueOperation<ConfigurationQueryQueueServiceParameters, ConfigurationQueryQueueServiceResult> left,
           BatchQueueOperation<ConfigurationQueryQueueServiceParameters, ConfigurationQueryQueueServiceResult> right)
        {
            return left.Kind == right.Kind && left.Argument.Parameters.Index == right.Argument.Parameters.Index;
        }
    }

    async Task ProcessDeleteConfigurationAsync(
        IEnumerable<BatchQueueOperation<ConfigurationQueryQueueServiceParameters, ConfigurationQueryQueueServiceResult>> array)
    {
        foreach (var operationGroup in array.Where(static x => x.Argument.Parameters.Index == 4)
                     .OrderByDescending(static x => x.Priority)
                     .GroupBy(static x => x.Argument.Type))
        {
            try
            {
                var type = operationGroup.Key;

                var funcKey = $"{type}-{nameof(CreateDeleteConfigurationLambdaExpression)}";
                if (!_funcDict.TryGetValue(funcKey, out var func))
                {
                    var expr = CreateDeleteConfigurationLambdaExpression(type);
                    func = expr.Compile();
                    _funcDict.TryAdd(funcKey, func);
                }


                foreach (var operation in operationGroup)
                {
                    try
                    {
                        var task = ((Func<object, ValueTask<ConfigurationSaveChangesResult>>)func).Invoke(operation.Argument.Parameters.AsT4.Value);
                        var saveChangesResult = await task;
                        operation.SetResult(new ConfigurationQueryQueueServiceResult(saveChangesResult));
                    }
                    catch (Exception ex)
                    {
                        operation.SetException(ex);
                        _exceptionCounter.AddOrUpdate(ex);
                        _logger.LogError(ex.ToString());
                    }

                }

            }
            catch (Exception ex)
            {
                foreach (var op in operationGroup) op.SetException(ex);
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
        }

    }

    async Task ProcessAddOrUpdateConfigurationAsync(
    IEnumerable<BatchQueueOperation<ConfigurationQueryQueueServiceParameters, ConfigurationQueryQueueServiceResult>> array)
    {
        foreach (var operationGroup in array
                     .Where(x => x.Argument.Parameters.Index == 4)
                     .OrderByDescending(x => x.Priority)
                     .GroupBy(x => x.Argument.Type))
        {
            try
            {
                var type = operationGroup.Key;
                var funcKey = $"{type}-{nameof(CreateAddOrUpdateLambdaExpression)}";
                if (!_funcDict.TryGetValue(funcKey, out var func))
                {
                    var expr = CreateAddOrUpdateLambdaExpression(type);
                    func = expr.Compile();
                    _funcDict.TryAdd(funcKey, func);
                }

                foreach (var operation in operationGroup)
                {
                    try
                    {
                        var task = ((Func<object, ValueTask<ConfigurationSaveChangesResult>>)func).Invoke(operation.Argument.Parameters.AsT4.Value);
                        var saveChangesResult = await task;
                        operation.SetResult(new ConfigurationQueryQueueServiceResult(saveChangesResult));
                    }
                    catch (Exception ex)
                    {
                        operation.SetException(ex);
                        _exceptionCounter.AddOrUpdate(ex);
                        _logger.LogError(ex.ToString());
                    }
                }

            }
            catch (Exception ex)
            {
                foreach (var op in operationGroup) op.SetException(ex);
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
        }

    }


    async Task ProcessQueryConfigurationByParameterAsync(
      IEnumerable<BatchQueueOperation<ConfigurationQueryQueueServiceParameters, ConfigurationQueryQueueServiceResult>> array)
    {
        foreach (var batchQueueOperationGroup in array
                     .Where(static x => x != null && x.Argument.Parameters.Index == 0)
                     .OrderByDescending(static x => x.Priority)
                     .GroupBy(static x => x.Argument))
        {
            try
            {
                var argument = batchQueueOperationGroup.Key;
                _logger.LogInformation(
                    $"QueryParameters:{argument.Parameters.AsT0},Type:{argument.Type},{batchQueueOperationGroup.Count()} requests");

                ListQueryResult<object> result = default;
                var funcKey = $"{argument.Type}-{nameof(CreateQueryConfigurationByParameterLambdaExpression)}";
                if (!_funcDict.TryGetValue(funcKey, out var func))
                {
                    var expr = CreateQueryConfigurationByParameterLambdaExpression(argument.Type);
                    func = expr.Compile();
                    _funcDict.TryAdd(funcKey, func);
                }

                var task = ((Func<PaginationQueryParameters, ValueTask<ListQueryResult<object>>>)func).Invoke(argument.Parameters.AsT0.Parameters);
                result = await task;
                foreach (var operation in batchQueueOperationGroup) operation.SetResult(new ConfigurationQueryQueueServiceResult(result));
            }
            catch (Exception ex)
            {
                foreach (var operation in batchQueueOperationGroup) operation.SetException(ex);
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
        }

    }

    async Task ProcessQueryConfigurationByIdAsync(IEnumerable<BatchQueueOperation<ConfigurationQueryQueueServiceParameters, ConfigurationQueryQueueServiceResult>> array)
    {
        foreach (var batchQueueOperationGroup in array
                     .Where(static x => x != null && x.Argument.Parameters.Index == 2)
                     .OrderByDescending(static x => x.Priority)
                     .GroupBy(static x => x.Argument.Type))
            try
            {
                var type = batchQueueOperationGroup.Key;
                var idList = batchQueueOperationGroup.SelectMany(static x => x.Argument.Parameters.AsT2.IdList);
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
                    {
                        if (hasValue)
                        {
                            var results = FindResults(queryResult.Items, op.Argument.Parameters.AsT2.IdList).ToArray();
                            op.SetResult(new ConfigurationQueryQueueServiceResult(
                                new ListQueryResult<object>(
                                results.Length,
                                1,
                                results.Length,
                                results)));
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
                foreach (var op in batchQueueOperationGroup) op.SetException(ex);
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
    }

    static IEnumerable<RecordBase> FindResults(
        IEnumerable<object> items,
        IEnumerable<string> idList)
    {
        foreach (var item in items)
        {
            if (item is not RecordBase model) continue;
            foreach (var id in idList)
            {
                if (model.Id == id)
                {
                    yield return model;
                }
            }
        }
        yield break;
    }

    Expression<Func<PaginationQueryParameters, ValueTask<ListQueryResult<object?>>>> CreateQueryConfigurationByParameterLambdaExpression(Type type)
    {
        var thisExpr = Expression.Constant(this);
        var queryParameters = Expression.Parameter(
            typeof(PaginationQueryParameters), "queryParameters");

        var callExpr = Expression.Call(thisExpr, nameof(QueryConfigurationByQueryParametersAsync), [type], queryParameters);

        return Expression.Lambda<Func<PaginationQueryParameters, ValueTask<ListQueryResult<object?>>>>(callExpr,
            queryParameters);
    }

    Expression<Func<PaginationQueryParameters, ValueTask<ListQueryResult<object?>>>> CreateQueryConfigurationVersionByParameterLambdaExpression(Type type)
    {
        var thisExpr = Expression.Constant(this);
        var queryParameters = Expression.Parameter(
            typeof(PaginationQueryParameters), "queryParameters");

        var callExpr = Expression.Call(thisExpr, nameof(QueryConfigurationVersionByQueryParametersAsync), [type], queryParameters);

        return Expression.Lambda<Func<PaginationQueryParameters, ValueTask<ListQueryResult<object?>>>>(callExpr,
            queryParameters);
    }

    Expression<Func<ConfigurationVersionSwitchParameters, ValueTask<ConfigurationSaveChangesResult>>> CreateConfigurationVersionSwitchLambdaExpression(Type type)
    {
        var thisExpr = Expression.Constant(this);
        var parameters = Expression.Parameter(typeof(ConfigurationVersionSwitchParameters), "parameters");

        var callExpr = Expression.Call(thisExpr, nameof(SwitchConfigurationVersionAsync), [type], parameters);

        return Expression.Lambda<Func<ConfigurationVersionSwitchParameters, ValueTask<ConfigurationSaveChangesResult>>>(callExpr,
            parameters);
    }

    Expression<Func<ConfigurationVersionDeleteParameters, ValueTask<ConfigurationVersionSaveChangesResult>>> CreateConfigurationVersionDeleteLambdaExpression(Type type)
    {
        var thisExpr = Expression.Constant(this);
        var parameters = Expression.Parameter(typeof(ConfigurationVersionDeleteParameters), "parameters");

        var callExpr = Expression.Call(thisExpr, nameof(DeleteConfigurationVersionAsync), [type], parameters);

        return Expression.Lambda<Func<ConfigurationVersionDeleteParameters, ValueTask<ConfigurationVersionSaveChangesResult>>>(callExpr,
            parameters);
    }

    Expression<Func<IEnumerable<string>, ValueTask<ListQueryResult<object?>>>> CreateQueryByIdListLambdaExpression(Type type)
    {
        var thisExpr = Expression.Constant(this);
        var idListParameter = Expression.Parameter(
            typeof(IEnumerable<string>), "idList");

        var callExpr = Expression.Call(thisExpr, nameof(QueryConfigurationByIdListAsync), [type], idListParameter);

        return Expression.Lambda<Func<IEnumerable<string>, ValueTask<ListQueryResult<object?>>>>(callExpr, idListParameter);
    }

    Expression<Func<object, ValueTask<ConfigurationSaveChangesResult>>>
    CreateAddOrUpdateLambdaExpression(Type type)
    {
        var thisExpr = Expression.Constant(this);
        var entityParameters = Expression.Parameter(typeof(object), "entity");

        var callExpr = Expression.Call(thisExpr, nameof(AddOrUpdateConfigurationAsync), [type], entityParameters);

        return Expression.Lambda<Func<object, ValueTask<ConfigurationSaveChangesResult>>>(callExpr, entityParameters);
    }

    Expression<Func<object, ValueTask<ConfigurationSaveChangesResult>>> CreateDeleteConfigurationLambdaExpression(Type type)
    {
        var thisExpr = Expression.Constant(this);
        var entityParameters = Expression.Parameter(typeof(object), "entity");

        var callExpr = Expression.Call(thisExpr, nameof(DeleteConfigurationAsync), [type], entityParameters);

        return Expression.Lambda<Func<object, ValueTask<ConfigurationSaveChangesResult>>>(callExpr, entityParameters);
    }

    async ValueTask<ListQueryResult<object>> QueryConfigurationByQueryParametersAsync<T>(
        PaginationQueryParameters queryParameters)
        where T : JsonRecordBase, IAggregateRoot
    {
        var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
        _logger.LogInformation($"{typeof(T).FullName}:{queryParameters}");
        ListQueryResult<T> listQueryResult = default;
        if (queryParameters.QueryStrategy == QueryStrategy.QueryPreferred)
        {
            using var configurationRepo = repoFactory.CreateRepository();
            listQueryResult = await configurationRepo.PaginationQueryAsync(
                new ConfigurationSelectSpecification<T, string>(queryParameters.Keywords, queryParameters.SortDescriptions),
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
                    new ConfigurationSelectSpecification<T, string>(
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
            listQueryResult.PageIndex,
            listQueryResult.PageSize,
            listQueryResult.Items.Select(x => x as object));
        return queryResult;
    }

    async ValueTask<ListQueryResult<object>> QueryConfigurationVersionByQueryParametersAsync<T>(
    PaginationQueryParameters queryParameters)
    where T : JsonRecordBase
    {
        var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<ConfigurationVersionRecordModel>>();
        _logger.LogInformation($"{typeof(T).FullName}:{queryParameters}");
        ListQueryResult<ConfigurationVersionRecordModel> listQueryResult = default;
        if (queryParameters.QueryStrategy == QueryStrategy.QueryPreferred)
        {
            using var repo = repoFactory.CreateRepository();
            listQueryResult = await repo.PaginationQueryAsync(
                new ConfigurationVersionSelectSpecification<string>(queryParameters.Keywords, queryParameters.SortDescriptions),
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
                new ConfigurationVersionSelectSpecification<string>(queryParameters.Keywords, queryParameters.SortDescriptions),
                    queryParameters.PageSize,
                    queryParameters.PageIndex);

                if (listQueryResult.HasValue)
                    _memoryCache.Set(key, listQueryResult, TimeSpan.FromMinutes(1));
            }
        }

        ListQueryResult<object> queryResult = new(
            listQueryResult.TotalCount,
            listQueryResult.PageIndex,
            listQueryResult.PageSize,
            listQueryResult.Items.Select(x => x as object));
        return queryResult;
    }

    async ValueTask<ListQueryResult<object>> QueryConfigurationByIdListAsync<T>(IEnumerable<string> idList)
        where T : JsonRecordBase, IAggregateRoot
    {
        var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
        using var repo = repoFactory.CreateRepository();
        _logger.LogInformation($"{typeof(T).FullName}:{string.Join(",", idList)}");
        var result = await repo.ListAsync(new ListSpecification<T>(DataFilterCollection<string>.Includes(idList)));
        ListQueryResult<object> queryResult = default;
        if (result != null)
            queryResult = new ListQueryResult<object>(
                result.Count,
                                1,
                result.Count,
                result.Select(static x => x as object));

        return queryResult;
    }

    async ValueTask<ConfigurationSaveChangesResult> AddOrUpdateConfigurationAsync<T>(object entityObject)
                where T : JsonRecordBase
    {
        var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
        using var configurationRepo = repoFactory.CreateRepository();
        var entity = entityObject as T;
        if (entity == null)
        {
            return default;
        }
        var entityFromDb = await configurationRepo.GetByIdAsync(entity.Id);

        try
        {
            T? oldEntity = null;
            var type = ConfigurationChangedType.None;
            var changesCount = 0;
            await ResilientTransaction.New(configurationRepo.DbContext).ExecuteAsync(async (transaction, cancellationToken) =>
            {

                if (entityFromDb == null)
                {
                    await configurationRepo.AddAsync(entity);
                    changesCount = 1;
                    entityFromDb = entity;
                    type = ConfigurationChangedType.Add;
                }
                else
                {
                    oldEntity = entityFromDb.JsonClone<T>();
                    entityFromDb.With(entity);
                    await configurationRepo.UpdateAsync(entityFromDb);
                    changesCount += configurationRepo.LastSaveChangesCount;
                    type = ConfigurationChangedType.Update;
                }
                if (changesCount > 0)
                {
                    await AddConfigurationVersionRecordAsync(entityFromDb, configurationRepo.DbContext);
                }
            });


            return new ConfigurationSaveChangesResult()
            {
                Type = type,
                ChangesCount = changesCount,
                OldValue = oldEntity,
                NewValue = entityFromDb
            };
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
            throw;
        }

        return default;
    }

    async ValueTask AddConfigurationVersionRecordAsync<T>(T entity, DbContext dbContext) where T : JsonRecordBase
    {
        var configurationRepo = new EfRepository<ConfigurationVersionRecordModel, DbContext>(dbContext);

        var configurationVersion = await configurationRepo.DbContext.Set<ConfigurationVersionRecordModel>()
            .Where(x => x.ConfigurationId == entity.Id)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync();

        var json = JsonSerializer.Serialize(entity.JsonClone<T>());
        var version = configurationVersion == null ? 1 : configurationVersion.Version + 1;

        await dbContext.Set<ConfigurationVersionRecordModel>()
            .Where(x => x.ConfigurationId == entity.Id)
            .ExecuteUpdateAsync(setPropertyCalls => setPropertyCalls.SetProperty(
                x => x.IsCurrent,
                x => x.Version == version));
        bool throwEx = false;
        if (throwEx)
        {
            throw new Exception();
        }
        await configurationRepo.AddAsync(new ConfigurationVersionRecordModel()
        {
            Id = Guid.NewGuid().ToString(),
            Name = entity.Name,
            ConfigurationId = entity.Id,
            IsCurrent = true,
            Version = version,
            Value = new ConfigurationVersionRecord() { FullName = typeof(T).FullName, Json = json }
        });
    }

    async ValueTask<ConfigurationSaveChangesResult> DeleteConfigurationAsync<T>(object entityObject)
            where T : JsonRecordBase
    {
        var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
        using var repo = repoFactory.CreateRepository();
        var entity = entityObject as T;
        await repo.DeleteAsync(entity);
        var applicationDbContext = repo.DbContext as ApplicationDbContext;
        await applicationDbContext.ConfigurationVersionRecordDbSet
            .Where(x => x.ConfigurationId == entity.Id)
            .ExecuteDeleteAsync();

        var type = ConfigurationChangedType.Delete;
        var changesCount = repo.LastSaveChangesCount;
        return new ConfigurationSaveChangesResult()
        {
            Type = type,
            OldValue = entity,
            ChangesCount = changesCount,
        };
    }

    async ValueTask<ConfigurationSaveChangesResult> SwitchConfigurationVersionAsync<T>(ConfigurationVersionSwitchParameters parameters)
        where T : JsonRecordBase
    {
        var configurationId = parameters.ConfigurationId;
        var targetVersion = parameters.TargetVersion;
        using var configVersionRepo = _configVersionRepoFactory.CreateRepository();
        var applicationDbContext = configVersionRepo.DbContext as ApplicationDbContext;
        var config = await configVersionRepo.FirstOrDefaultAsync(new ConfigurationVersionSelectSpecification<ConfigurationVersionRecordModel>(
            parameters.ConfigurationId,
            parameters.TargetVersion));
        if (config == null)
        {
            return default;
        }
        var value = JsonSerializer.Deserialize(config.Value.Json, typeof(T)) as T;
        if (value == null)
        {
            return default;
        }
        var dbSet = configVersionRepo.DbContext.Set<T>();
        var targetConfig = await dbSet.FindAsync(parameters.ConfigurationId);
        if (targetConfig == null)
        {
            await applicationDbContext.ConfigurationVersionRecordDbSet
                .Where(x => x.ConfigurationId == configurationId)
                .ExecuteDeleteAsync();

            return default;
        }
        var oldConfig = targetConfig with { };
        targetConfig.With(value);
        var changesCount = await configVersionRepo.SaveChangesAsync();
        if (changesCount > 0)
        {

            await applicationDbContext.ConfigurationVersionRecordDbSet
                .Where(x => x.ConfigurationId == configurationId)
                .ExecuteUpdateAsync(setPropertyCalls => setPropertyCalls.SetProperty(x => x.IsCurrent, x => x.Version == targetVersion));
        }
        return new ConfigurationSaveChangesResult()
        {
            ChangesCount = changesCount,
            NewValue = targetConfig,
            OldValue = oldConfig,
            Type = ConfigurationChangedType.Update,
        };
        return default;
    }


    async ValueTask<ConfigurationVersionSaveChangesResult> DeleteConfigurationVersionAsync<T>(ConfigurationVersionDeleteParameters parameters)
               where T : JsonRecordBase
    {
        try
        {
            using var configVersionRepo = _configVersionRepoFactory.CreateRepository();
            await configVersionRepo.DeleteAsync(parameters.Value);
            return new ConfigurationVersionSaveChangesResult()
            {
                ChangesCount = configVersionRepo.LastSaveChangesCount,
                VersionRecord = parameters.Value,
                Type = ConfigurationChangedType.Delete,
            };
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
        return default;
    }


}