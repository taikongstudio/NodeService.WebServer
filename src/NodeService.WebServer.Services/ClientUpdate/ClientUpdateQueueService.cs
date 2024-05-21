using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.ClientUpdate
{
    public class ClientUpdateQueueService : BackgroundService
    {
        private readonly ILogger<ClientUpdateQueueService> _logger;
        private readonly IMemoryCache _memoryCache;
        private readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepoFactory;
        private readonly ExceptionCounter _exceptionCounter;

        public ClientUpdateQueueService(
            ILogger<ClientUpdateQueueService> logger,
            ExceptionCounter exceptionCounter,
            ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepoFactory,
            IMemoryCache memoryCache
            )
        {
            _logger = logger;
            _memoryCache = memoryCache;
            _nodeInfoRepoFactory = nodeInfoRepoFactory;
            _exceptionCounter = exceptionCounter;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await QueueAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(30));
            }
        }

        private async Task QueueAsync(CancellationToken stoppingToken)
        {
            try
            {
                var nodeInfoRepo = _nodeInfoRepoFactory.CreateRepository();
                var nodeList = await nodeInfoRepo.ListAsync(stoppingToken);
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
                for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
                {
                    var items = nodeArray.Skip(pageIndex * pageSize).Take(pageSize);
                    foreach (var item in items)
                    {
                        var key = $"ClientUpdateEnabled:{item.Profile.IpAddress}";
                        _memoryCache.Set(key, true, TimeSpan.FromMinutes(2));
                    }
                    await Task.Delay(TimeSpan.FromMinutes(2));
                }
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }

        }

    }
}
