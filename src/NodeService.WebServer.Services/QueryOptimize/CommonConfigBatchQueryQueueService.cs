using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Data;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using OneOf;
using System.Collections.Immutable;
using System.Configuration;
using System.Linq.Expressions;

namespace NodeService.WebServer.Services.QueryOptimize;

public record struct CommonConfigQueryQueueServiceParameters
{
    public CommonConfigQueryQueueServiceParameters(Type type, ConfigurationVersionPaginationQueryParameters queryParameters)
    {
        Parameters = queryParameters;
        Type = type;
    }

    public CommonConfigQueryQueueServiceParameters(Type type, ConfigurationPaginationQueryParameters queryParameters)
    {
        Parameters = queryParameters;
        Type = type;
    }

    public CommonConfigQueryQueueServiceParameters(Type type, ConfigurationIdentityQueryParameters queryParameters)
    {
        Parameters = queryParameters;
        Type = type;
    }

    public CommonConfigQueryQueueServiceParameters(Type type, ConfigurationVersionIdentityQueryParameters queryParameters)
    {
        Parameters = queryParameters;
        Type = type;
    }

    public CommonConfigQueryQueueServiceParameters(Type type, ConfigurationAddUpdateDeleteParameters   parameters)
    {
        Parameters = parameters;
        Type = type;
    }

    public CommonConfigQueryQueueServiceParameters(Type type, ConfigurationVersionSwitchParameters  parameters)
    {
        Parameters = parameters;
        Type = type;
    }

    public CommonConfigQueryQueueServiceParameters(Type type, ConfigurationVersionDeleteParameters  parameters)
    {
        Parameters = parameters;
        Type = type;
    }

    public OneOf<
        ConfigurationPaginationQueryParameters,
        ConfigurationVersionPaginationQueryParameters,
        ConfigurationIdentityQueryParameters,
        ConfigurationVersionIdentityQueryParameters,
        ConfigurationAddUpdateDeleteParameters,
        ConfigurationVersionSwitchParameters,
        ConfigurationVersionDeleteParameters> Parameters
    { get; private set; }

    public Type Type { get; private set; }
}

public record struct CommonConfigQueryQueueServiceResult
{
    public CommonConfigQueryQueueServiceResult(ListQueryResult<object> result)
    {
        Value = result;
    }

    public CommonConfigQueryQueueServiceResult(ConfigurationSaveChangesResult saveChangesResult)
    {
        Value = saveChangesResult;
    }

    public CommonConfigQueryQueueServiceResult(ConfigurationVersionSaveChangesResult saveChangesResult)
    {
        Value = saveChangesResult;
    }

    public OneOf<ListQueryResult<object>, ConfigurationSaveChangesResult, ConfigurationVersionSaveChangesResult> Value { get; private set; }
}


public class CommonConfigBatchQueryQueueService : BackgroundService
{
    readonly BatchQueue<BatchQueueOperation<CommonConfigQueryQueueServiceParameters, CommonConfigQueryQueueServiceResult>> _batchQueue;

    readonly IServiceProvider _serviceProvider;
    readonly IMemoryCache _memoryCache;
    readonly ApplicationRepositoryFactory<ConfigurationVersionRecordModel> _configVersionRepoFactory;
    readonly ILogger<CommonConfigBatchQueryQueueService> _logger;
    readonly ExceptionCounter _exceptionCounter;
    readonly ConcurrentDictionary<string, Delegate> _funcDict;

    public CommonConfigBatchQueryQueueService(
        ILogger<CommonConfigBatchQueryQueueService> logger,
        ExceptionCounter exceptionCounter,
        IServiceProvider serviceProvider,
        BatchQueue<BatchQueueOperation<CommonConfigQueryQueueServiceParameters, CommonConfigQueryQueueServiceResult>> batchQueue,
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

    async Task ProcessDeleteConfigurationVersionAsync(ImmutableArray<BatchQueueOperation<CommonConfigQueryQueueServiceParameters, CommonConfigQueryQueueServiceResult>> array)
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

    async Task ProcessSwitchConfigurationVersionAsync(ImmutableArray<BatchQueueOperation<CommonConfigQueryQueueServiceParameters, CommonConfigQueryQueueServiceResult>> array)
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

    async Task ProcessQueryConfigurationVersionByParameterAsync(ImmutableArray<BatchQueueOperation<CommonConfigQueryQueueServiceParameters, CommonConfigQueryQueueServiceResult>> array)
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

    async Task ProcessDeleteConfigurationAsync(
        IEnumerable<BatchQueueOperation<CommonConfigQueryQueueServiceParameters, CommonConfigQueryQueueServiceResult>> array)
    {
        foreach (var operationGroup in array.Where(static x => x.Argument.Parameters.Index == 4)
                     .OrderByDescending(static x => x.Priority)
                     .GroupBy(static x => x.Argument.Type))
        {
            try
            {
                var type = operationGroup.Key;

                foreach (var operation in operationGroup)
                {
                    var funcKey = $"{type}-{nameof(CreateDeleteConfigurationLambdaExpression)}";
                    if (!_funcDict.TryGetValue(funcKey, out var func))
                    {
                        var expr = CreateDeleteConfigurationLambdaExpression(type);
                        func = expr.Compile();
                        _funcDict.TryAdd(funcKey, func);
                    }

                    var task = ((Func<object, ValueTask<ConfigurationSaveChangesResult>>)func).Invoke(operation.Argument.Parameters.AsT4.Value);
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

    }

    async Task ProcessAddOrUpdateConfigurationAsync(
    IEnumerable<BatchQueueOperation<CommonConfigQueryQueueServiceParameters, CommonConfigQueryQueueServiceResult>> array)
    {
        foreach (var operationGroup in array
                     .Where(x => x.Argument.Parameters.Index == 4)
                     .OrderByDescending(x => x.Priority)
                     .GroupBy(x => x.Argument.Type))
        {
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

                    var task = ((Func<object, ValueTask<ConfigurationSaveChangesResult>>)func).Invoke(operation.Argument.Parameters.AsT4.Value);
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

    }


    async Task ProcessQueryConfigurationByParameterAsync(
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
                var funcKey = $"{argument.Type}-{nameof(CreateQueryConfigurationByParameterLambdaExpression)}";
                if (!_funcDict.TryGetValue(funcKey, out var func))
                {
                    var expr = CreateQueryConfigurationByParameterLambdaExpression(argument.Type);
                    func = expr.Compile();
                    _funcDict.TryAdd(funcKey, func);
                }

                var task = ((Func<PaginationQueryParameters, ValueTask<ListQueryResult<object>>>)func).Invoke(argument.Parameters.AsT0.Parameters);
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

    async Task ProcessQueryConfigurationByIdAsync(IEnumerable<BatchQueueOperation<CommonConfigQueryQueueServiceParameters, CommonConfigQueryQueueServiceResult>> array)
    {
        foreach (var batchQueueOperationGroup in array
                     .Where(static x => x != null && x.Argument.Parameters.Index == 2)
                     .OrderByDescending(static x => x.Priority)
                     .GroupBy(static x => x.Argument.Type))
            try
            {
                var type = batchQueueOperationGroup.Key;
                var idList = batchQueueOperationGroup.Select(static x => x.Argument.Parameters.AsT2.Id);
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
                        if (hasValue && TryFindItem(queryResult.Items, op.Argument.Parameters.AsT2.Id, out var result))
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

    static bool TryFindItem(
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
        var entityParameters = Expression.Parameter( typeof(object), "entity");

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
        where T : JsonBasedDataModel, IAggregateRoot
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

    async ValueTask<ListQueryResult<object>> QueryConfigurationVersionByQueryParametersAsync<T>(
    PaginationQueryParameters queryParameters)
    where T : LightJsonBasedDataModel
    {
        var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<ConfigurationVersionRecordModel>>();
        _logger.LogInformation($"{typeof(T).FullName}:{queryParameters}");
        ListQueryResult<ConfigurationVersionRecordModel> listQueryResult = default;
        if (queryParameters.QueryStrategy == QueryStrategy.QueryPreferred)
        {
            using var repo = repoFactory.CreateRepository();
            listQueryResult = await repo.PaginationQueryAsync(
                new ConfigurationVersionSpecification<ConfigurationVersionRecordModel>(queryParameters.Keywords, queryParameters.SortDescriptions),
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
                new ConfigurationVersionSpecification<ConfigurationVersionRecordModel>(queryParameters.Keywords, queryParameters.SortDescriptions),
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

    async ValueTask<ListQueryResult<object>> QueryConfigurationByIdListAsync<T>(IEnumerable<string> idList)
        where T : JsonBasedDataModel, IAggregateRoot
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
                result.Select(static x => x as object));

        return queryResult;
    }

    async ValueTask<ConfigurationSaveChangesResult> AddOrUpdateConfigurationAsync<T>(object entityObject)
                where T : JsonBasedDataModel
    {
        var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
        using var configRepo = repoFactory.CreateRepository();
        var entity = entityObject as T;
        var entityFromDb = await configRepo.GetByIdAsync(entity.Id);
        var type = ConfigurationChangedType.None;
        var changesCount = 0;
        if (entityFromDb == null)
        {
            await configRepo.AddAsync(entity);
            changesCount = 1;
            entityFromDb = entity;
            type = ConfigurationChangedType.Add;
        }
        else
        {
            entityFromDb.With(entity);
            await configRepo.UpdateAsync(entityFromDb);
            changesCount += configRepo.LastChangesCount;
            type = ConfigurationChangedType.Update;
        }
        if (changesCount > 0)
        {
            await AddConfigurationVersionRecordAsync(entityFromDb);
        }

        return new ConfigurationSaveChangesResult()
        {
            Type = type,
            ChangesCount = changesCount,
            Entity = entityFromDb
        };
    }

    async ValueTask AddConfigurationVersionRecordAsync<T>(T entity) where T : JsonBasedDataModel
    {
        try
        {
            using var configVersionRepo = _configVersionRepoFactory.CreateRepository();
            var applicationDbContext = configVersionRepo.DbContext as ApplicationDbContext;
            var configurationVersion = await applicationDbContext.ConfigurationVersionRecordDbSet
                .Where(x => x.ConfigurationId == entity.Id)
                .OrderByDescending(x => x.Version)
                .FirstOrDefaultAsync();
            
            var json = JsonSerializer.Serialize(entity.JsonClone<T>());
            var version = configurationVersion == null ? 1 : configurationVersion.Version + 1;
            await applicationDbContext.ConfigurationVersionRecordDbSet
                .Where(x => x.ConfigurationId == entity.Id)
                .ExecuteUpdateAsync(setPropertyCalls => setPropertyCalls.SetProperty(x => x.IsCurrent, x => x.Version == version));
            await configVersionRepo.AddAsync(new ConfigurationVersionRecordModel()
            {
                Id = Guid.NewGuid().ToString(),
                ConfigurationId = entity.Id,
                IsCurrent = true,
                Version = version,
                Value = new ConfigurationVersionRecord() { FullName = typeof(T).FullName, Json = json }
            });
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }

    }

    async ValueTask<ConfigurationSaveChangesResult> DeleteConfigurationAsync<T>(object entityObject)
            where T : JsonBasedDataModel
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
        var changesCount = repo.LastChangesCount;
        return new ConfigurationSaveChangesResult()
        {
            Type = type,
            ChangesCount = changesCount,
        };
    }

    async ValueTask<ConfigurationSaveChangesResult> SwitchConfigurationVersionAsync<T>(ConfigurationVersionSwitchParameters parameters)
        where T : JsonBasedDataModel
    {
        try
        {
            var configurationId = parameters.ConfigurationId;
            var targetVersion = parameters.TargetVersion;
            using var configVersionRepo = _configVersionRepoFactory.CreateRepository();
            var config = await configVersionRepo.FirstOrDefaultAsync(new ConfigurationVersionSpecification<ConfigurationVersionRecordModel>(
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
                return default;
            }
            targetConfig.With(value);
            var changesCount = await configVersionRepo.SaveChangesAsync();
            if (changesCount > 0)
            {
                var applicationDbContext = configVersionRepo.DbContext as ApplicationDbContext;
                await applicationDbContext.ConfigurationVersionRecordDbSet
                    .Where(x => x.ConfigurationId == configurationId)
                    .ExecuteUpdateAsync(setPropertyCalls => setPropertyCalls.SetProperty(x => x.IsCurrent, x => x.Version == targetVersion));
            }
            return new ConfigurationSaveChangesResult()
            {
                ChangesCount = changesCount,
                Entity = targetConfig,
                Type = ConfigurationChangedType.Update,
            };
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
        return default;
    }


     async ValueTask<ConfigurationVersionSaveChangesResult> DeleteConfigurationVersionAsync<T>(ConfigurationVersionDeleteParameters parameters)
                where T : JsonBasedDataModel
    {
        try
        {
            using var configVersionRepo = _configVersionRepoFactory.CreateRepository();
            await configVersionRepo.DeleteAsync(parameters.Value);
            return new ConfigurationVersionSaveChangesResult()
            {
                ChangesCount = configVersionRepo.LastChangesCount,
                Entity = parameters.Value,
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