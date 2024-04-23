using Microsoft.EntityFrameworkCore;
using NodeService.Infrastructure;
using NodeService.Infrastructure.Models;
using NodeService.WebServer.Data;

namespace NodeService.WebServerTools.Services
{
    public class AnalysisLogService : BackgroundService
    {
        private readonly ILogger<AnalysisLogService> _logger;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ApiService _apiService;

        public AnalysisLogService(
            ILogger<AnalysisLogService> logger,
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            ApiService apiService
            )
        {
            _logger = logger;
            _dbContextFactory = dbContextFactory;
            _apiService = apiService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            await dbContext.Database.EnsureCreatedAsync();
            foreach (var jobExecutionInstance in await dbContext.JobExecutionInstancesDbSet.AsQueryable()
                .Where(x => x.JobScheduleConfigId == "b1fe6c08-505f-46fa-aa03-92c9d48d73c7")
                .ToArrayAsync())
            {
                var logMessageEntries = await _apiService.QueryJobExecutionInstanceLogAsync(jobExecutionInstance.Id,QueryParameters.All);
                foreach (var logMessageEntry in logMessageEntries.Result)
                {
                    if (logMessageEntry.Value == null)
                    {
                        continue;
                    }
                    if (logMessageEntry.Value.Contains("Exception"))
                    {
                        _logger.LogInformation(logMessageEntry.Value);
                    }
                }
            }
        }

    }
}
