﻿using Microsoft.EntityFrameworkCore;
using NodeService.Infrastructure.DataModels;
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
            if (item.Value.PenddingLimitTimeSeconds == 0)
            {
                item.Value.PenddingLimitTimeSeconds = 600;
            }
            if (item.Value.ExecutionLimitTimeSeconds == 0)
            {
                item.Value.PenddingLimitTimeSeconds = 600;
            }
        }

        var count1 = await dbContext.SaveChangesAsync();
    }
}