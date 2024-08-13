using Microsoft.EntityFrameworkCore;
using NodeService.Infrastructure.DataModels;
using NodeService.WebServer.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
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
        try
        {
            var dbContext = _dbContextFactory.CreateDbContext();

            var ndoeServiceUpdateServiceConfig = File.OpenRead("D:\\format\\NodeService.UpdateService");
            var ndoeServiceWindowsServiceConfig = File.OpenRead("D:\\format\\NodeService.WindowsService");
            var ndoeServiceWorkerServiceConfig = File.OpenRead("D:\\format\\NodeService.WorkerService");
            var ndoeServiceServiceHostConfig = File.OpenRead("D:\\format\\NodeService.ServiceHost");
            dbContext.ClientUpdateConfigurationDbSet.Add(JsonSerializer.Deserialize<ClientUpdateConfigModel>(ndoeServiceUpdateServiceConfig));
            dbContext.ClientUpdateConfigurationDbSet.Add(JsonSerializer.Deserialize<ClientUpdateConfigModel>(ndoeServiceWindowsServiceConfig));
            dbContext.ClientUpdateConfigurationDbSet.Add(JsonSerializer.Deserialize<ClientUpdateConfigModel>(ndoeServiceWorkerServiceConfig));
            dbContext.ClientUpdateConfigurationDbSet.Add(JsonSerializer.Deserialize<ClientUpdateConfigModel>(ndoeServiceServiceHostConfig));
            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {

        }

    }
}