using Microsoft.EntityFrameworkCore;
using NodeService.Infrastructure;
using NodeService.Infrastructure.Models;
using NodeService.WebServer.Data;

namespace NodeService.WebServerTools.Services;

public class AnalysisLogService : BackgroundService
{
    private readonly ApiService _apiService;
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ILogger<AnalysisLogService> _logger;

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

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        await dbContext.Database.EnsureCreatedAsync();
        try
        {
            using var streamWriter = new StreamWriter(File.Open("d:\\task.txt", FileMode.OpenOrCreate));

            foreach (var jobExecutionInstance in await dbContext.JobExecutionInstancesDbSet.AsQueryable()
                         .Where(x => x.Name.Contains("075404"))
                         .ToArrayAsync())
            {
                jobExecutionInstance.NodeInfo =
                    await dbContext.NodeInfoDbSet.FirstOrDefaultAsync(x => x.Id == jobExecutionInstance.NodeInfoId);

                var pageIndex = 0;
                var pageSize = 512;


                var rsp = await _apiService.QueryTaskExecutionInstanceLogAsync(jobExecutionInstance.Id,
                    new PaginationQueryParameters());

                if (!rsp.Result.Any()) continue;
                var ok = rsp.Result.Any(x => x.Value.Contains("2024-04-12T15:10:37"));
                if (ok) continue;
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