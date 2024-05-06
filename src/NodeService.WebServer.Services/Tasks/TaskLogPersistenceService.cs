using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Models;

namespace NodeService.WebServer.Services.Tasks;

public class TaskLogPersistenceService : BackgroundService
{
    private readonly ILogger<TaskLogPersistenceService> _logger;
    private readonly TaskLogCacheManager _taskLogCacheManager;
    private readonly ExceptionCounter _exceptionCounter;

    public TaskLogPersistenceService(
        ILogger<TaskLogPersistenceService> logger,
        TaskLogCacheManager taskLogCacheManager,
        ExceptionCounter exceptionCounter
    )
    {
        _logger = logger;
        _taskLogCacheManager = taskLogCacheManager;
        _exceptionCounter = exceptionCounter;
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
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }

            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }
}