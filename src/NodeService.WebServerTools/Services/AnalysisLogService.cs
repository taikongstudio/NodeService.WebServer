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
            try
            {
                using var streamWriter = new StreamWriter(File.Open("d:\\task.txt", FileMode.OpenOrCreate));

                foreach (var jobExecutionInstance in await dbContext.JobExecutionInstancesDbSet.AsQueryable()
                    .Where(x => x.Name.Contains("8354021"))
                    .ToArrayAsync())
                {

                    jobExecutionInstance.NodeInfo = await dbContext.NodeInfoDbSet.FirstOrDefaultAsync(x => x.Id == jobExecutionInstance.NodeInfoId);

                    int pageIndex = 0;
                    int pageSize = 512;



                    var rsp = await _apiService.QueryJobExecutionInstanceLogAsync(jobExecutionInstance.Id,
                        new QueryParameters(pageIndex, pageSize));

                    if (!rsp.Result.Any())
                    {
                        continue;
                    }
                    var ok = rsp.Result.Any(x => x.Value.Contains(""));
                    if (ok)
                    {
                        continue;
                    }
                    streamWriter.Write(jobExecutionInstance.NodeInfo.Name);
                    streamWriter.Write("\t\t");
                    streamWriter.WriteLine(ok);
                    pageIndex++;

                }
                streamWriter.Flush();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
            }

        }

    }
}
