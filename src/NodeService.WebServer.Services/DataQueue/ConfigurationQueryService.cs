using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using NodeService.Infrastructure.Data;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.TaskSchedule;

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
        ImmutableArray<string> idList,
        CancellationToken cancellationToken = default)
        where T : JsonRecordBase
    {
        if (idList.IsDefaultOrEmpty)
        {
            return default;
        }
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
        idList = [..idList.Except(resultList.Select(static x => x.Id)),];
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
        ConfigurationVersionRecordModel? configVersionRecord = null;
        await ResilientTransaction.New(configurationRepo.DbContext).ExecuteAsync(async (transaction, cancellationToken) =>
        {

            if (entityFromDb == null)
            {
                await configurationRepo.AddAsync(entity, cancellationToken);
                changesCount = 1;
                entityFromDb = entity;
                type = ConfigurationChangedType.Add;
            }
            else
            {
                oldEntity = entityFromDb.JsonClone<T>();
                entityFromDb.With(entity);
                await configurationRepo.UpdateAsync(entityFromDb, cancellationToken);
                changesCount += configurationRepo.LastSaveChangesCount;
                type = ConfigurationChangedType.Update;
            }
            if (changesCount > 0)
            {
                if (updateConfigurationRecord)
                {
                    configVersionRecord = await UpdateConfigurationVersionRecordAsync(
                        entityFromDb,
                        configurationRepo.DbContext,
                        cancellationToken);
                }
                else
                {
                    configVersionRecord = await AddConfigurationVersionRecordAsync(
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
            NewValue = entityFromDb,
            ConfigurationVersionRecord = configVersionRecord
        };
    }


    async ValueTask<ConfigurationVersionRecordModel> AddConfigurationVersionRecordAsync<T>(
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
        return configVersion;
    }

    async ValueTask<ConfigurationVersionRecordModel?> UpdateConfigurationVersionRecordAsync<T>(
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
            return null;
        }

        var json = JsonSerializer.Serialize(entity.JsonClone<T>());
        configurationVersion.Value.Json = json;
        configurationRepo.DbContext.Set<ConfigurationVersionRecordModel>().Update(configurationVersion);
        await configurationRepo.SaveChangesAsync(cancellationToken);
        return configurationVersion;
    }

    public async ValueTask<ConfigurationSaveChangesResult> DeleteConfigurationAsync<T>(
        T entity,
        CancellationToken cancellationToken = default)
            where T : JsonRecordBase
    {
        var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<T>>();
        await using var repo = await repoFactory.CreateRepositoryAsync(cancellationToken);
        var count = await repo.DbContext.Set<ConfigurationVersionRecordModel>()
            .Where(x => x.ConfigurationId == entity.Id && x.ReferenceCount > 0)
            .CountAsync(cancellationToken);

        if (count > 0)
        {
            return new ConfigurationSaveChangesResult()
            {
                Type = ConfigurationChangedType.None,
                OldValue = entity,
                ChangesCount = 0,
            };
        }

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

    public async ValueTask<ConfigurationSaveChangesResult> SwitchConfigurationVersionAsync<T>(
        ConfigurationVersionSwitchParameters parameters,
        CancellationToken cancellationToken = default)
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
        if (parameters.Value.ReferenceCount > 0)
        {
            return new ConfigurationVersionSaveChangesResult()
            {
                ChangesCount = 0,
                VersionRecord = parameters.Value,
                Type = ConfigurationChangedType.None,
            };
        }
        await configVersionRepo.DeleteAsync(parameters.Value, cancellationToken);
        return new ConfigurationVersionSaveChangesResult()
        {
            ChangesCount = configVersionRepo.LastSaveChangesCount,
            VersionRecord = parameters.Value,
            Type = ConfigurationChangedType.Delete,
        };
    }

    public async ValueTask AddConfigurationVersionReferenceAsync<T>(
        string configurationId,
        int targetVersion,
        CancellationToken cancellationToken = default)
        where T : JsonRecordBase
    {
        await IncreaseConfigurationVersionAsync(
            configurationId,
            targetVersion,
            1,
            cancellationToken);
    }

    private async ValueTask IncreaseConfigurationVersionAsync(
        string configurationId,
        int targetVersion,
        int increasement,
        CancellationToken cancellationToken)
    {
        await using var configVersionRepo = await _configVersionRepoFactory.CreateRepositoryAsync(cancellationToken);
        var count = await configVersionRepo.DbContext.Set<ConfigurationVersionRecordModel>()
            .Where(x => x.ConfigurationId == configurationId && x.Version == targetVersion && x.ReferenceCount > 0)
            .ExecuteUpdateAsync(setPropertyCalls => setPropertyCalls.SetProperty(x => x.ReferenceCount, x => x.ReferenceCount + increasement));
    }

    public async ValueTask<ConfigurationVersionRecordModel?> GetConfigurationCurrentVersionAsync(
    string configurationId,
    CancellationToken cancellationToken)
    {
        await using var configVersionRepo = await _configVersionRepoFactory.CreateRepositoryAsync(cancellationToken);
        var configVersionRecord = await configVersionRepo.DbContext.Set<ConfigurationVersionRecordModel>()
            .Where(x => x.ConfigurationId == configurationId && x.IsCurrent)
            .FirstOrDefaultAsync();
        return configVersionRecord;
    }

    public async ValueTask ReleaseConfigurationVersionReferenceAsync<T>(
        string configurationId,
        int targetVersion,
        CancellationToken cancellationToken)
    where T : JsonRecordBase
    {
        await IncreaseConfigurationVersionAsync(configurationId, targetVersion, -1, cancellationToken);
    }

    public async ValueTask<TaskObservationConfiguration?> QueryTaskObservationConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var result = await _objectCache.GetObjectAsync<TaskObservationConfiguration?>(
            NotificationSources.TaskObservation,
            cancellationToken);

        if (result != null)
        {
            return result;
        }

        var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<PropertyBag>>();
        await using var repo = await repoFactory.CreateRepositoryAsync(cancellationToken);
        var propertyBag = await repo.FirstOrDefaultAsync(new PropertyBagSpecification(NotificationSources.TaskObservation), cancellationToken);
        if (propertyBag == null)
        {
            result = new TaskObservationConfiguration();
            result.InitDefaults();
            propertyBag = new PropertyBag
            {
                { "Id", NotificationSources.TaskObservation },
                { "Value", JsonSerializer.Serialize(result) }
            };
            propertyBag["CreatedDate"] = DateTime.UtcNow;
            bool fix = false;
            if (fix)
            {
                await repo.UpdateAsync(propertyBag, cancellationToken);
            }
            else
            {
                await repo.AddAsync(propertyBag, cancellationToken);
            }

        }
        else
        {
            result = JsonSerializer.Deserialize<TaskObservationConfiguration>(propertyBag["Value"] as string);
        }
        return result;
    }

    public async ValueTask UpdateTaskObservationConfigurationAsync(
        TaskObservationConfiguration entity,
        CancellationToken cancellationToken = default)
    {
        var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<PropertyBag>>();
        await using var repo = await repoFactory.CreateRepositoryAsync(cancellationToken);
        var propertyBag = await repo.FirstOrDefaultAsync(new PropertyBagSpecification(NotificationSources.TaskObservation), cancellationToken);
        propertyBag["Value"] = JsonSerializer.Serialize(entity);
        await repo.SaveChangesAsync(cancellationToken);
        await _objectCache.SetObjectAsync(NotificationSources.TaskObservation, entity, cancellationToken);
        var queue = _serviceProvider.GetService<IAsyncQueue<AsyncOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>>>();
        var parameters = new TaskScheduleServiceParameters(new TaskObservationScheduleParameters(NotificationSources.TaskObservation));
        var op = new AsyncOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>(parameters, AsyncOperationKind.AddOrUpdate);
        await queue.EnqueueAsync(op, cancellationToken);
        await op.WaitAsync(cancellationToken);
    }

    public async ValueTask<NodeHealthyCheckConfiguration> QueryNodeHealthyCheckConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var result = await _objectCache.GetObjectAsync<NodeHealthyCheckConfiguration?>(
            NotificationSources.NodeHealthyCheck,
            cancellationToken);

        if (result != null)
        {
            return result;
        }

        var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<PropertyBag>>();
        await using var repo = await repoFactory.CreateRepositoryAsync(cancellationToken);
        var propertyBag =
            await repo.FirstOrDefaultAsync(new PropertyBagSpecification(NotificationSources.NodeHealthyCheck), cancellationToken);
        if (propertyBag == null)
        {
            result = new NodeHealthyCheckConfiguration();
            propertyBag = new PropertyBag
            {
                { "Id", NotificationSources.NodeHealthyCheck },
                { "Value", JsonSerializer.Serialize(result) }
            };
            propertyBag["CreatedDate"] = DateTime.UtcNow;
            await repo.AddAsync(propertyBag, cancellationToken);
        }
        else
        {
            result = JsonSerializer.Deserialize<NodeHealthyCheckConfiguration>(propertyBag["Value"] as string);
        }
        return result;
    }

    public async ValueTask UpdateNodeHealthyCheckConfigurationAsync(
    NodeHealthyCheckConfiguration  entity,
    CancellationToken cancellationToken = default)
    {
        var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<PropertyBag>>();
        await using var repo = await repoFactory.CreateRepositoryAsync(cancellationToken);
        var propertyBag = await repo.FirstOrDefaultAsync(
            new PropertyBagSpecification(NotificationSources.NodeHealthyCheck),
            cancellationToken);
        propertyBag["Value"] = JsonSerializer.Serialize(entity);
        await repo.SaveChangesAsync(cancellationToken);
        await _objectCache.SetObjectAsync(NotificationSources.NodeHealthyCheck, entity, cancellationToken);
        var queue = _serviceProvider.GetService<IAsyncQueue<AsyncOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>>>();
        var parameters = new TaskScheduleServiceParameters(new NodeHealthyCheckScheduleParameters(NotificationSources.NodeHealthyCheck));
        var op = new AsyncOperation<TaskScheduleServiceParameters, TaskScheduleServiceResult>(parameters, AsyncOperationKind.AddOrUpdate);
        await queue.EnqueueAsync(op, cancellationToken);
        await op.WaitAsync(cancellationToken);

    }

    public async ValueTask<TaskDefinitionModel?> GetTaskDefinitionAsync(
        string taskDefinitionId,
        CancellationToken cancellationToken = default)
    {
        var list = await this.QueryConfigurationByIdListAsync<TaskDefinitionModel>([taskDefinitionId], cancellationToken);
        return list.Items?.FirstOrDefault();
    }

    public async ValueTask<TaskTypeDescConfigModel?> GetTaskTypeDescAsync(
        string taskTypeDescId,
        CancellationToken cancellationToken = default)
    {
        var list = await this.QueryConfigurationByIdListAsync<TaskTypeDescConfigModel>([taskTypeDescId], cancellationToken);
        return list.Items?.FirstOrDefault();
    }

    public async ValueTask UpdateNodeSettingsAsync(NodeSettings entity, CancellationToken cancellationToken = default)
    {
        var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<PropertyBag>>();
        await using var repo = await repoFactory.CreateRepositoryAsync(cancellationToken);
        var propertyBag = await repo.FirstOrDefaultAsync(new PropertyBagSpecification(nameof(NodeSettings)));

        var value = JsonSerializer.Serialize(entity);
        var count = await repo.DbContext.Set<PropertyBag>()
            .Where(x => x["Id"] == nameof(NodeSettings))
            .ExecuteUpdateAsync(setPropertyCalls => setPropertyCalls.SetProperty(x => x["Value"], x => value),
                cancellationToken: cancellationToken);
        _memoryCache.Remove(nameof(NodeSettings));
        await _objectCache.SetObjectAsync(nameof(NodeSettings), entity, cancellationToken);
    }


    public async ValueTask<NodeSettings?> QueryNodeSettingsAsync(CancellationToken cancellationToken = default)
    {
        NodeSettings? result = await _objectCache.GetObjectAsync<NodeSettings>(nameof(NodeSettings), cancellationToken);
        if (result == null)
        {
            var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<PropertyBag>>();
            await using var propertyBagRepo = await repoFactory.CreateRepositoryAsync(cancellationToken);
            var propertyBag =
                await propertyBagRepo.FirstOrDefaultAsync(new PropertyBagSpecification(nameof(NodeSettings)), cancellationToken);
            if (propertyBag == null)
            {
                result = new NodeSettings();
                propertyBag = new PropertyBag();
                propertyBag.Add("Id", "NodeSettings");
                propertyBag.Add("Value", JsonSerializer.Serialize(result));
                propertyBag.Add("CreatedDate", DateTime.UtcNow);
                await propertyBagRepo.AddAsync(propertyBag, cancellationToken);
            }
            else
            {
                result = JsonSerializer.Deserialize<NodeSettings>(propertyBag["Value"] as string);
            }
            await _objectCache.SetObjectAsync(nameof(NodeSettings), result, cancellationToken);
        }
        return result;
    }
}