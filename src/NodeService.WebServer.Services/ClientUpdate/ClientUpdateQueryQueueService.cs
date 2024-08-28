using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;

namespace NodeService.WebServer.Services.ClientUpdate;

public class ClientUpdateBatchQueryParameters
{
    public ClientUpdateBatchQueryParameters(string name, string ipAddress)
    {
        Name = name;
        IpAddress = ipAddress;
    }

    public string Name { get; private set; }
    public string IpAddress { get; private set; }
}

public class ClientUpdateQueryQueueService : BackgroundService
{
    private readonly ILogger<ClientUpdateQueryQueueService> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepoFactory;
    private readonly ApplicationRepositoryFactory<ClientUpdateConfigModel> _clientUpdateRepoFactory;
    private readonly ExceptionCounter _exceptionCounter;

    private readonly BatchQueue<AsyncOperation<ClientUpdateBatchQueryParameters, ClientUpdateConfigModel>>
        _batchQueue;

    public ClientUpdateQueryQueueService(
        ILogger<ClientUpdateQueryQueueService> logger,
        ExceptionCounter exceptionCounter,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepoFactory,
        ApplicationRepositoryFactory<ClientUpdateConfigModel> clientUpdateRepoFactory,
        BatchQueue<AsyncOperation<ClientUpdateBatchQueryParameters, ClientUpdateConfigModel>> batchQueue,
        IMemoryCache memoryCache
    )
    {
        _logger = logger;
        _memoryCache = memoryCache;
        _nodeInfoRepoFactory = nodeInfoRepoFactory;
        _clientUpdateRepoFactory = clientUpdateRepoFactory;
        _exceptionCounter = exceptionCounter;
        _batchQueue = batchQueue;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(
            ShuffleNodesAsync(cancellationToken),
            QueueAsync(cancellationToken));
    }


    async Task ShuffleNodesAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
            try
            {
                int itemsCount = await GetNodesCount(cancellationToken);
                var nodeNumbers = Enumerable.Range(0, itemsCount);
                if (Debugger.IsAttached) _memoryCache.Set(CreateKey("::1"), true, TimeSpan.FromMinutes(10));
                int pageIndex = 1;
                foreach (var array in nodeNumbers.Chunk(5))
                {
                    var nodeList = await QueryNodesListAsync(pageIndex, cancellationToken);
                    foreach (var item in nodeList.Items)
                    {
                        var key = CreateKey(item.Profile.IpAddress);
                        _memoryCache.Set(key, true, TimeSpan.FromMinutes(5));
                    }

                    await Task.Delay(TimeSpan.FromSeconds(60 * 5 - 5), cancellationToken);
                    pageIndex++;
                }
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
            finally
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
    }

    async ValueTask<ListQueryResult<NodeInfoModel>> QueryNodesListAsync(int pageIndex, CancellationToken cancellationToken = default)
    {
        await using var nodeInfoRepo = await _nodeInfoRepoFactory.CreateRepositoryAsync();
        var listQueryResult = await nodeInfoRepo.PaginationQueryAsync(
                                        new NodeInfoSpecification(),
                                        new PaginationInfo(pageIndex, 20),
                                        cancellationToken);
        return listQueryResult;
    }

    async Task<int> GetNodesCount(CancellationToken cancellationToken)
    {
        await using var nodeInfoRepo = await _nodeInfoRepoFactory.CreateRepositoryAsync();
        return await nodeInfoRepo.CountAsync(cancellationToken);
    }

    private static string CreateKey(string ipAddress)
    {
        return $"ClientUpdateEnabled:{ipAddress}";
    }

    async Task QueueAsync(CancellationToken cancellationToken = default)
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
                await using var nodeInfoRepo = await _nodeInfoRepoFactory.CreateRepositoryAsync();
                foreach (var nameGroup in array.GroupBy(static x => x.Argument.Name))
                {
                    var name = nameGroup.Key;

                    var key = $"ClientUpdateConfig:{name}";
                    ClientUpdateConfigModel? clientUpdateConfig = null;
                    foreach (var op in nameGroup)
                        try
                        {
                            var ipAddress = op.Argument.IpAddress;
                            var updateKey = CreateKey(ipAddress);

                            if (_memoryCache.TryGetValue<bool>(updateKey, out var isEnabled))
                            {
                                if (clientUpdateConfig == null)
                                    clientUpdateConfig = await QueryAsync(name, key);
                                clientUpdateConfig = await IsFiltered(clientUpdateConfig, ipAddress);
                                op.TrySetResult(clientUpdateConfig);
                            }
                            else
                            {
                                var hasNodeInfo = await nodeInfoRepo.AnyAsync(new NodeInfoSpecification(
                                        null,
                                        op.Argument.IpAddress,
                                        NodeDeviceType.Computer),
                                    cancellationToken);
                                if (!hasNodeInfo)
                                {
                                    if (clientUpdateConfig == null)
                                        clientUpdateConfig = await QueryAsync(name, key);
                                    op.TrySetResult(clientUpdateConfig);
                                }
                                else
                                {
                                    op.TrySetResult(null);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            op.TrySetException(ex);
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
                stopwatch.Stop();
                _logger.LogInformation($"{array.Length} requests,Ellapsed:{stopwatch.Elapsed}");
            }
        }
    }

    private async Task<ClientUpdateConfigModel?> IsFiltered(ClientUpdateConfigModel? clientUpdateConfig,
        string ipAddress)
    {
        if (clientUpdateConfig != null && clientUpdateConfig.DnsFilters != null && clientUpdateConfig.DnsFilters.Any())
        {
            var key = $"ClientUpdateConfig:Filter:{clientUpdateConfig.Id}:{ipAddress}";
            if (!_memoryCache.TryGetValue<bool>(key, out var value))
            {
                await using var nodeInfoRepo = await _nodeInfoRepoFactory.CreateRepositoryAsync();

                var dataFilterType = DataFilterTypes.None;

                if (clientUpdateConfig.DnsFilterType.Equals("include", StringComparison.OrdinalIgnoreCase))
                    dataFilterType = DataFilterTypes.Include;
                else if (clientUpdateConfig.DnsFilterType.Equals("exclude", StringComparison.OrdinalIgnoreCase))
                    dataFilterType = DataFilterTypes.Exclude;

                var nodeList = await nodeInfoRepo.ListAsync(new NodeInfoSpecification(
                    AreaTags.Any,
                    NodeStatus.All,
                    NodeDeviceType.Computer,
                    new DataFilterCollection<string>(
                        dataFilterType,
                        clientUpdateConfig.DnsFilters.Select(x => x.Value)),
                    default));
                if (nodeList.Count != 0 && nodeList.Any(x => x.Profile.IpAddress == ipAddress))
                {
                    if (dataFilterType == DataFilterTypes.Exclude) clientUpdateConfig = null;
                }
                else
                {
                    clientUpdateConfig = null;
                }

                value = clientUpdateConfig != null;
                _memoryCache.Set(key, value, TimeSpan.FromMinutes(10));
            }

            if (!value) clientUpdateConfig = null;
        }

        return clientUpdateConfig;
    }

    private async Task<ClientUpdateConfigModel?> QueryAsync(string name, string key)
    {
        ClientUpdateConfigModel? clientUpdateConfig = null;
        if (!_memoryCache.TryGetValue<ClientUpdateConfigModel>(key, out var cacheValue) || cacheValue == null)
        {
            await using var repo = await _clientUpdateRepoFactory.CreateRepositoryAsync();

            cacheValue =
                await repo.FirstOrDefaultAsync(
                    new ClientUpdateConfigSpecification(ClientUpdateStatus.Public, name));

            if (cacheValue != null) _memoryCache.Set(key, cacheValue, TimeSpan.FromMinutes(2));
        }

        if (cacheValue != null) clientUpdateConfig = cacheValue;

        return clientUpdateConfig;
    }
}