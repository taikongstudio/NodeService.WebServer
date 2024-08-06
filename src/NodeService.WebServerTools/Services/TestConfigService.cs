using Microsoft.EntityFrameworkCore;
using NodeService.WebServer.Data;

namespace NodeService.WebServerTools.Services
{
    internal class TestConfigService : BackgroundService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ILogger<ClearConfigService> _logger;

        public TestConfigService(
            ILogger<ClearConfigService> logger,
            IDbContextFactory<ApplicationDbContext> dbContextFactory)
        {
            _logger = logger;
            _dbContextFactory = dbContextFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var dbContext = _dbContextFactory.CreateDbContext();
            var ftpUploadConfigs = await dbContext.TaskFlowExecutionInstanceDbSet.ToListAsync();
            foreach (var item in ftpUploadConfigs)
            {
                if (item.Value.TaskStages.All(x=>x.Status== Infrastructure.DataModels.TaskFlowExecutionStatus.Finished))
                {
                    item.Status = Infrastructure.DataModels.TaskFlowExecutionStatus.Finished;
                }

            }
            await dbContext.SaveChangesAsync();
        }
    }
}
