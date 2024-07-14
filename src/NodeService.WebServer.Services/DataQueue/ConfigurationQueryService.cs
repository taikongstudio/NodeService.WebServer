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


public class ConfigurationQueryService 
{
    readonly IServiceProvider _serviceProvider;
    readonly IMemoryCache _memoryCache;
    readonly ApplicationRepositoryFactory<ConfigurationVersionRecordModel> _configVersionRepoFactory;
    readonly ILogger<ConfigurationQueryService> _logger;
    readonly ExceptionCounter _exceptionCounter;
    readonly ConcurrentDictionary<string, Delegate> _funcDict;

    public ConfigurationQueryService(
        ILogger<ConfigurationQueryService> logger,
        ExceptionCounter exceptionCounter,
        IServiceProvider serviceProvider,
        IMemoryCache memoryCache,
        ApplicationRepositoryFactory<ConfigurationVersionRecordModel> configVersionRepoFactory
    )
    {
        _logger = logger;
        _exceptionCounter = exceptionCounter;
        _serviceProvider = serviceProvider;
        _memoryCache = memoryCache;
        _configVersionRepoFactory = configVersionRepoFactory;
        _funcDict = new ConcurrentDictionary<string, Delegate>();
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
    PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)

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
        var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
        await using var repo = await repoFactory.CreateRepositoryAsync(cancellationToken);
        _logger.LogInformation($"{typeof(T).FullName}:{string.Join(",", idList)}");
        var result = await repo.ListAsync(
            new ListSpecification<T>(DataFilterCollection<string>.Includes(idList)),
            cancellationToken);
        ListQueryResult<T> queryResult = default;
        if (result != null)
        {
            queryResult = new ListQueryResult<T>(
                result.Count,
                1,
                result.Count,
                result);
        }


        return queryResult;
    }

    public async ValueTask<ConfigurationSaveChangesResult> AddOrUpdateConfigurationAsync<T>(
        T entity,
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
                await AddConfigurationVersionRecordAsync(
                    entityFromDb,
                    configurationRepo.DbContext,
                    cancellationToken);
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

    public async ValueTask<ConfigurationSaveChangesResult> DeleteConfigurationAsync<T>(
        T entity,
        CancellationToken cancellationToken = default)
            where T : JsonRecordBase
    {
        var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
        await using var repo = await repoFactory.CreateRepositoryAsync(cancellationToken);
        await repo.DeleteAsync(entity, cancellationToken);
        var applicationDbContext = repo.DbContext as ApplicationDbContext;
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