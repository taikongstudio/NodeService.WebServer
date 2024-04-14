using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Logging;
using NodeService.WebServer.Models;

namespace NodeService.WebServer.Services.Tasks
{
    public class TaskLogPersistenceService : BackgroundService
    {
        private readonly ILogger<TaskLogPersistenceService> _logger;
        private readonly TaskLogCacheManager _taskLogCacheManager;

        public TaskLogPersistenceService(
            ILogger<TaskLogPersistenceService> logger,
            TaskLogCacheManager taskLogCacheManager
            )
        {
            _logger = logger;
            _taskLogCacheManager = taskLogCacheManager;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _taskLogCacheManager.Flush();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.ToString());
                }

                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }

    }
}
