using Microsoft.EntityFrameworkCore;
using NodeService.WebServer.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServerTools.Services;

internal class ClearConfigService : BackgroundService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ILogger<ClearConfigService> _logger;

    public ClearConfigService(
        ILogger<ClearConfigService> logger,
        IDbContextFactory<ApplicationDbContext> dbContextFactory)
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var dbContext = _dbContextFactory.CreateDbContext();
        var ftpUploadConfigs = await dbContext.TaskDefinitionDbSet.ToListAsync();
        foreach (var item in ftpUploadConfigs)
        {
            item.Value.PenddingLimitTimeSeconds = 300;
            item.Value.RetryDuration = 300;
            item.Value.MaxRetryCount = 10;
            item.Value.ExecutionStrategy = Infrastructure.DataModels.TaskExecutionStrategy.Concurrent;

            dbContext.Update(item);
        }

        await foreach (var item in dbContext.NodeFileSyncRecordDbSet.AsAsyncEnumerable())
        {

            item.Value.FileInfo.Length;
        }

        var count = await dbContext.SaveChangesAsync();
    }
}