﻿using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Models;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.QueryOptimize
{
    public class ClientUpdateQueueService : BackgroundService
    {
        private readonly ILogger<ClientUpdateQueueService> _logger;
        private readonly IMemoryCache _memoryCache;
        private readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepoFactory;
        private readonly ApplicationRepositoryFactory<ClientUpdateConfigModel> _clientUpdateRepoFactory;
        private readonly ExceptionCounter _exceptionCounter;
        private readonly BatchQueue<BatchQueueOperation<ClientUpdateBatchQueryParameters, ClientUpdateConfigModel>> _batchQueue;

        public ClientUpdateQueueService(
            ILogger<ClientUpdateQueueService> logger,
            ExceptionCounter exceptionCounter,
            ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepoFactory,
            ApplicationRepositoryFactory<ClientUpdateConfigModel> clientUpdateRepoFactory,
            BatchQueue<BatchQueueOperation<ClientUpdateBatchQueryParameters, ClientUpdateConfigModel>> batchQueue,
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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _ = Task.Factory.StartNew((state) => ShuffleNodesAsync((CancellationToken)state), stoppingToken, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await QueueAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(30));
            }
        }

        async Task ShuffleNodesAsync(CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using var nodeInfoRepo = _nodeInfoRepoFactory.CreateRepository();
                    var nodeList = await nodeInfoRepo.ListAsync(cancellationToken);
                    var nodeArray = nodeList.ToArray();
                    Random.Shared.Shuffle(nodeArray);
                    Random.Shared.Shuffle(nodeArray);
                    Random.Shared.Shuffle(nodeArray);
                    int pageSize = 20;
                    int pageCount = Math.DivRem(nodeList.Count, pageSize, out var result);
                    if (result > 0)
                    {
                        pageCount += 1;
                    }
                    if (Debugger.IsAttached)
                    {
                        _memoryCache.Set(CreateKey("::1"), true, TimeSpan.FromMinutes(2));
                    }
                    for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
                    {
                        var items = nodeArray.Skip(pageIndex * pageSize).Take(pageSize);
                        foreach (var item in items)
                        {
                            string key = CreateKey(item.Profile.IpAddress);
                            _memoryCache.Set(key, true, TimeSpan.FromMinutes(2));
                        }
                        await Task.Delay(TimeSpan.FromMinutes(2), cancellationToken);
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
        }

        private static string CreateKey(string value)
        {
            return $"ClientUpdateEnabled:{value}";
        }

        private async Task QueueAsync(CancellationToken stoppingToken = default)
        {


            await foreach (var arrayPoolCollection in _batchQueue.ReceiveAllAsync(stoppingToken))
            {
                var count = arrayPoolCollection.CountNotDefault();
                if (count == 0)
                {
                    continue;
                }
                Stopwatch stopwatch = Stopwatch.StartNew();
                try
                {

                    foreach (var nameGroup in arrayPoolCollection.Where(static x => x != null).GroupBy(static x => x.Argument.Name))
                    {
                        var name = nameGroup.Key;

                        var key = $"ClientUpdateConfig:{name}";
                        ClientUpdateConfigModel? clientUpdateConfig = null;
                        foreach (var op in nameGroup)
                        {
                            try
                            {
                                var ipAddress = op.Argument.IpAddress;
                                var updateKey = CreateKey(ipAddress);
                                if (!_memoryCache.TryGetValue<bool>(updateKey, out var isEnabled))
                                {
                                    op.SetResult(null);
                                }
                                else
                                {
                                    if (clientUpdateConfig == null)
                                    {
                                        clientUpdateConfig = await QueryAsync(name, key, ipAddress);
                                    }
                                    clientUpdateConfig = await IsFiltered(clientUpdateConfig, ipAddress);
                                    op.SetResult(clientUpdateConfig);
                                }
                            }
                            catch (Exception ex)
                            {
                                op.SetException(ex);
                                _exceptionCounter.AddOrUpdate(ex);
                                _logger.LogError(ex.ToString());
                            }

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

        private async Task<ClientUpdateConfigModel?> IsFiltered(ClientUpdateConfigModel? clientUpdateConfig, string ipAddress)
        {
            if (clientUpdateConfig != null && clientUpdateConfig.DnsFilters != null && clientUpdateConfig.DnsFilters.Any())
            {
                var key = $"ClientUpdateConfig:Filter:{clientUpdateConfig.Id}:{ipAddress}";
                if (!_memoryCache.TryGetValue<bool>(key, out var value))
                {
                    using var nodeInfoRepo = _nodeInfoRepoFactory.CreateRepository();

                    var dataFilterType = DataFilterTypes.None;

                    if (clientUpdateConfig.DnsFilterType.Equals("include", StringComparison.OrdinalIgnoreCase))
                        dataFilterType = DataFilterTypes.Include;
                    else if (clientUpdateConfig.DnsFilterType.Equals("exclude", StringComparison.OrdinalIgnoreCase))
                        dataFilterType = DataFilterTypes.Exclude;

                    var nodeList = await nodeInfoRepo.ListAsync(new NodeInfoSpecification(
                        AreaTags.Any,
                        NodeStatus.All,
                            new DataFilterCollection<string>(
                            dataFilterType,
                            clientUpdateConfig.DnsFilters.Select(x => x.Value)),
                        default));
                    if (nodeList.Count != 0 && nodeList.Any(x => x.Profile.IpAddress == ipAddress))
                    {
                        if (dataFilterType == DataFilterTypes.Exclude)
                        {
                            clientUpdateConfig = null;
                        }
                    }
                    else
                    {
                        clientUpdateConfig = null;
                    }
                    value = clientUpdateConfig != null;
                    _memoryCache.Set(key, value, TimeSpan.FromMinutes(10));
                }
                if (!value)
                {
                    clientUpdateConfig = null;
                }
            }

            return clientUpdateConfig;
        }

        private async Task<ClientUpdateConfigModel?> QueryAsync(string name, string key, string ipAddress)
        {
            ClientUpdateConfigModel? clientUpdateConfig = null;
            if (!_memoryCache.TryGetValue<ClientUpdateConfigModel>(key, out var cacheValue) || cacheValue == null)
            {
                using var repo = _clientUpdateRepoFactory.CreateRepository();

                cacheValue =
                    await repo.FirstOrDefaultAsync(
                        new ClientUpdateConfigSpecification(ClientUpdateStatus.Public, name));

                if (cacheValue != null) _memoryCache.Set(key, cacheValue, TimeSpan.FromMinutes(2));
            }

            if (cacheValue != null) clientUpdateConfig = cacheValue;

            return clientUpdateConfig;
        }

    }
}