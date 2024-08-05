using Microsoft.Extensions.DependencyInjection;
using NodeService.Infrastructure.Data;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using OneOf;

namespace NodeService.WebServer.Services.DataQueue;

public class ConfigurationQueryService 
{
    readonly IServiceProvider _serviceProvider;
    readonly IMemoryCache _memoryCache;
    readonly ObjectCache _objectCache;
    readonly ApplicationRepositoryFactory<ConfigurationVersionRecordModel> _configVersionRepoFactory;
    readonly ILogger<ConfigurationQueryService> _logger;
    readonly ExceptionCounter _exceptionCounter;


    public ConfigurationQueryService(
        ILogger<ConfigurationQueryService> logger,
        ExceptionCounter exceptionCounter,
        IServiceProvider serviceProvider,
        IMemoryCache memoryCache,
        ObjectCache objectCache,
        ApplicationRepositoryFactory<ConfigurationVersionRecordModel> configVersionRepoFactory
    )
    {
        _logger = logger;
        _exceptionCounter = exceptionCounter;
        _serviceProvider = serviceProvider;
        _memoryCache = memoryCache;
        _objectCache = objectCache;
        _configVersionRepoFactory = configVersionRepoFactory;
    }

    public async ValueTask<ListQueryResult<T>> QueryConfigurationByQueryParametersAsync<T>(
        PaginationQueryParameters queryParameters,
        CancellationToken cancellationToken = default)
         where T : JsonRecordBase
    {
        var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
        _logger.LogInformation($"{typeof(T).FullName}:{queryParameters}");
        ListQueryResult<T> listQueryResult = default;
        if (queryParameters.QueryStrategy == QueryStrategy.QueryPreferred)
        {
            await using var configurationRepo = await repoFactory.CreateRepositoryAsync(cancellationToken);
            listQueryResult = await configurationRepo.PaginationQueryAsync(
                new ConfigurationSelectSpecification<T, string>(queryParameters.Keywords, queryParameters.SortDescriptions),
                new PaginationInfo(queryParameters.PageIndex, queryParameters.PageSize),
                cancellationToken);
            if (listQueryResult.HasValue)
            {
                foreach (var entity in listQueryResult.Items)
                {
                    await _objectCache.SetEntityAsync(entity, cancellationToken);
                }
            }
        }
        else if (queryParameters.QueryStrategy == QueryStrategy.CachePreferred)
        {
            var key = $"{typeof(T).FullName}:{queryParameters}";

            if (!_memoryCache.TryGetValue(key, out listQueryResult) || !listQueryResult.HasValue)
            {
                await using var repo = await repoFactory.CreateRepositoryAsync(cancellationToken);
                listQueryResult = await repo.PaginationQueryAsync(
                    new ConfigurationSelectSpecification<T, string>(
                        queryParameters.Keywords,
                        queryParameters.SortDescriptions),
                new PaginationInfo(
                    queryParameters.PageIndex,
                    queryParameters.PageSize),
                cancellationToken);

                if (listQueryResult.HasValue)
                    _memoryCache.Set(key, listQueryResult, TimeSpan.FromMinutes(1));
            }
        }

        return listQueryResult;
    }

    public async ValueTask<ListQueryResult<ConfigurationVersionRecordModel>> QueryConfigurationVersionListByQueryParametersAsync(
        PaginationQueryParameters queryParameters,
        CancellationToken cancellationToken = default)

    {
        var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<ConfigurationVersionRecordModel>>();

        ListQueryResult<ConfigurationVersionRecordModel> listQueryResult = default;
        if (queryParameters.QueryStrategy == QueryStrategy.QueryPreferred)
        {
            await using var repo = await repoFactory.CreateRepositoryAsync(cancellationToken);
            listQueryResult = await repo.PaginationQueryAsync(
                new ConfigurationVersionSelectSpecification<string>(queryParameters.Keywords, queryParameters.SortDescriptions),
                new PaginationInfo(queryParameters.PageIndex, queryParameters.PageSize),
                cancellationToken);
            if (listQueryResult.HasValue)
            {
                foreach (var entity in listQueryResult.Items)
                {
                    await _objectCache.SetEntityAsync(entity, cancellationToken);
                }
            }
        }
        else if (queryParameters.QueryStrategy == QueryStrategy.CachePreferred)
        {
            var key = $"{QueryConfigurationVersionListByQueryParametersAsync}:{queryParameters}";

            if (!_memoryCache.TryGetValue(key, out listQueryResult) || !listQueryResult.HasValue)
            {
                await using var repo = await repoFactory.CreateRepositoryAsync();
                listQueryResult = await repo.PaginationQueryAsync(
                new ConfigurationVersionSelectSpecification<string>(queryParameters.Keywords, queryParameters.SortDescriptions),
                     new PaginationInfo(queryParameters.PageIndex, queryParameters.PageSize),
                cancellationToken);

                if (listQueryResult.HasValue)
                    _memoryCache.Set(key, listQueryResult, TimeSpan.FromMinutes(1));
            }
        }

        return listQueryResult;
    }

    public async ValueTask<ListQueryResult<T>> QueryConfigurationByIdListAsync<T>(
        IEnumerable<string> idList,
        CancellationToken cancellationToken = default)
        where T : JsonRecordBase
    {

        _logger.LogInformation($"{typeof(T).FullName}:{string.Join(",", idList)}");
        var resultList = new List<T>();
        foreach (var id in idList)
        {
            var entity = await _objectCache.GetEntityAsync<T>(id, cancellationToken);
            if (entity == null)
            {
                continue;
            }
            resultList.Add(entity);
        }
        idList = idList.Except(resultList.Select(static x => x.Id)).ToArray();
        if (idList.Any())
        {
            var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
            await using var repo = await repoFactory.CreateRepositoryAsync(cancellationToken);

            var list = await repo.ListAsync(
               new ListSpecification<T>(DataFilterCollection<string>.Includes(idList)),
               cancellationToken);
            foreach (var item in list)
            {
                await _objectCache.SetEntityAsync(item, cancellationToken);
            }

            resultList = resultList.Union(list).ToList();
        }

        ListQueryResult<T> queryResult = default;
        if (resultList != null)
        {
            queryResult = new ListQueryResult<T>(
                resultList.Count,
                1,
                resultList.Count,
                resultList);
        }

        return queryResult;
    }

    public async ValueTask<ConfigurationSaveChangesResult> AddOrUpdateConfigurationAsync<T>(
        T entity,
        bool updateConfigurationRecord = false,
        CancellationToken cancellationToken = default)
                where T : JsonRecordBase
    {
        ArgumentNullException.ThrowIfNull(entity);
        var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
        await using var configurationRepo = await repoFactory.CreateRepositoryAsync(cancellationToken);
        var entityFromDb = await configurationRepo.GetByIdAsync(entity.Id, cancellationToken);

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
                if (updateConfigurationRecord)
                {
                    await UpdateConfigurationVersionRecordAsync(
                        entityFromDb,
                        configurationRepo.DbContext,
                        cancellationToken);
                }
                else
                {
                    await AddConfigurationVersionRecordAsync(
                        entityFromDb,
                        configurationRepo.DbContext,
                        cancellationToken);
                }

                await _objectCache.SetEntityAsync(entityFromDb, cancellationToken);
            }
        }, cancellationToken);




        return new ConfigurationSaveChangesResult()
        {
            Type = type,
            ChangesCount = changesCount,
            OldValue = oldEntity,
            NewValue = entityFromDb
        };
    }

    async ValueTask AddConfigurationVersionRecordAsync<T>(
        T entity,
        DbContext dbContext,
        CancellationToken cancellationToken = default) where T : JsonRecordBase
    {
        var configurationRepo = new EFRepository<ConfigurationVersionRecordModel, DbContext>(dbContext);

        var configurationVersion = await configurationRepo.DbContext.Set<ConfigurationVersionRecordModel>()
            .Where(x => x.ConfigurationId == entity.Id)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(cancellationToken: cancellationToken);

        var json = JsonSerializer.Serialize(entity.JsonClone<T>());
        var version = configurationVersion == null ? 1 : configurationVersion.Version + 1;

        await dbContext.Set<ConfigurationVersionRecordModel>()
            .Where(x => x.ConfigurationId == entity.Id)
            .ExecuteUpdateAsync(setPropertyCalls => setPropertyCalls.SetProperty(
                x => x.IsCurrent,
                x => x.Version == version), cancellationToken);
        bool throwEx = false;
        if (throwEx)
        {
            throw new Exception();
        }
        var configVersion = new ConfigurationVersionRecordModel()
        {
            Id = Guid.NewGuid().ToString(),
            Name = entity.Name,
            ConfigurationId = entity.Id,
            IsCurrent = true,
            Version = version,
            Value = new ConfigurationVersionRecord() { FullName = typeof(T).FullName, Json = json }
        };
        await configurationRepo.AddAsync(configVersion, cancellationToken);
    }

    async ValueTask UpdateConfigurationVersionRecordAsync<T>(
    T entity,
    DbContext dbContext,
    CancellationToken cancellationToken = default) where T : JsonRecordBase
    {
        var configurationRepo = new EFRepository<ConfigurationVersionRecordModel, DbContext>(dbContext);

        var configurationVersion = await configurationRepo.DbContext.Set<ConfigurationVersionRecordModel>()
            .Where(x => x.ConfigurationId == entity.Id && x.IsCurrent)
            .FirstOrDefaultAsync(cancellationToken: cancellationToken);

        if (configurationVersion == null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(entity.JsonClone<T>());
        configurationVersion.Value.Json = json;
        configurationRepo.DbContext.Set<ConfigurationVersionRecordModel>().Update(configurationVersion);
        await configurationRepo.SaveChangesAsync(cancellationToken);
    }

    public async ValueTask<ConfigurationSaveChangesResult> DeleteConfigurationAsync<T>(
        T entity,
        CancellationToken cancellationToken = default)
            where T : JsonRecordBase
    {
        var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
        await using var repo = await repoFactory.CreateRepositoryAsync(cancellationToken);
        await repo.DeleteAsync(entity, cancellationToken);
        var applicationDbContext = repo.DbContext as ApplicationDbContext;
        await RemoveConfigurationVersionCache(entity, applicationDbContext, cancellationToken);
        await applicationDbContext.ConfigurationVersionRecordDbSet
            .Where(x => x.ConfigurationId == entity.Id)
            .ExecuteDeleteAsync(cancellationToken);

        var type = ConfigurationChangedType.Delete;
        var changesCount = repo.LastSaveChangesCount;
        return new ConfigurationSaveChangesResult()
        {
            Type = type,
            OldValue = entity,
            ChangesCount = changesCount,
        };
    }

    async ValueTask RemoveConfigurationVersionCache<T>(
        T entity,
        ApplicationDbContext applicationDbContext,
        CancellationToken cancellationToken) where T : JsonRecordBase
    {
        var configurationVersionIdList = await applicationDbContext.ConfigurationVersionRecordDbSet.Where(x => x.ConfigurationId == entity.Id)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        foreach (var configurationVersionId in configurationVersionIdList)
        {
            var key = _objectCache.GetEntityKey<ConfigurationVersionRecordModel>(configurationVersionId);
            await _objectCache.RemoveObjectAsync(key, cancellationToken);
        }
    }

    public async ValueTask<ConfigurationSaveChangesResult> SwitchConfigurationVersionAsync<T>(ConfigurationVersionSwitchParameters parameters, CancellationToken cancellationToken = default)
        where T : JsonRecordBase
    {
        var configurationId = parameters.ConfigurationId;
        var targetVersion = parameters.TargetVersion;
        await using var configVersionRepo = await _configVersionRepoFactory.CreateRepositoryAsync(cancellationToken);
        var applicationDbContext = configVersionRepo.DbContext as ApplicationDbContext;
        var config = await configVersionRepo.FirstOrDefaultAsync(new ConfigurationVersionSelectSpecification<ConfigurationVersionRecordModel>(
            parameters.ConfigurationId,
            parameters.TargetVersion), cancellationToken);
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
        var targetConfig = await dbSet.FindAsync([parameters.ConfigurationId], cancellationToken);
        if (targetConfig == null)
        {
            await applicationDbContext.ConfigurationVersionRecordDbSet
                .Where(x => x.ConfigurationId == configurationId)
                .ExecuteDeleteAsync(cancellationToken);

            return default;
        }
        var oldConfig = targetConfig with { };
        targetConfig.With(value);
        var changesCount = await configVersionRepo.SaveChangesAsync(cancellationToken);
        if (changesCount > 0)
        {

            await applicationDbContext.ConfigurationVersionRecordDbSet
                .Where(x => x.ConfigurationId == configurationId)
                .ExecuteUpdateAsync(
                setPropertyCalls => setPropertyCalls.SetProperty(x => x.IsCurrent, x => x.Version == targetVersion),
                cancellationToken);
        }
        return new ConfigurationSaveChangesResult()
        {
            ChangesCount = changesCount,
            NewValue = targetConfig,
            OldValue = oldConfig,
            Type = ConfigurationChangedType.Update,
        };
    }


    public async ValueTask<ConfigurationVersionSaveChangesResult> DeleteConfigurationVersionAsync<T>(
        ConfigurationVersionDeleteParameters parameters,
        CancellationToken cancellationToken = default)
               where T : JsonRecordBase
    {
        await using var configVersionRepo = await _configVersionRepoFactory.CreateRepositoryAsync(cancellationToken);
        await configVersionRepo.DeleteAsync(parameters.Value, cancellationToken);
        return new ConfigurationVersionSaveChangesResult()
        {
            ChangesCount = configVersionRepo.LastSaveChangesCount,
            VersionRecord = parameters.Value,
            Type = ConfigurationChangedType.Delete,
        };
    }


}