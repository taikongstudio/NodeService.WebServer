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
        var ftpUploadConfigs = await dbContext.FtpUploadConfigurationDbSet.ToListAsync();
        foreach (var item in ftpUploadConfigs)
        {
            item.FtpConfig = null;
            item.Value.MatchType = MatchType.Win32;
            dbContext.Update(item);
        }

        var count = await dbContext.SaveChangesAsync();
    }
}