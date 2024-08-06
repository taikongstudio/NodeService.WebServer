using Microsoft.EntityFrameworkCore;
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
        var ftpUploadConfigs = await dbContext.NodeInfoDbSet.ToListAsync();

        Dictionary<string, List<NodeInfoModel>> usageDict = new Dictionary<string, List<NodeInfoModel>>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in ftpUploadConfigs)
        {
            if (item.Profile.Usages == null)
            {
                continue;
            }
            string factoryCode = "BL";
            if (item.Profile.IpAddress.StartsWith("172."))
            {
                factoryCode = "GM";
            }
            else if (item.Profile.IpAddress.StartsWith("10."))
            {
                factoryCode = "BL";
            }
            var uages = item.Profile.Usages.Split(",", StringSplitOptions.RemoveEmptyEntries);
            foreach (var usage in uages.Where(x => !x.StartsWith("BL") && !x.StartsWith("GM")))
            {
                var key = $"{factoryCode}-{usage}";
                if (!usageDict.TryGetValue(key, out var list))
                {
                    list = new List<NodeInfoModel>();
                    usageDict.Add(key, list);
                }
                list.Add(item);
            }
        }

        foreach (var item in usageDict)
        {
            var nodeUsageConfigurationModel = await dbContext.NodeUsageConfigurationDbSet.FirstOrDefaultAsync(x => x.Name == item.Key);
            nodeUsageConfigurationModel.Value.Nodes = nodeUsageConfigurationModel.Value.Nodes.Union(item.Value.Select(x => new NodeUsageInfo()
            {
                Name = x.Name,
                NodeInfoId = x.Id
            })).Distinct().ToList();
            nodeUsageConfigurationModel.Value = nodeUsageConfigurationModel.Value with { };
            dbContext.NodeUsageConfigurationDbSet.Update(nodeUsageConfigurationModel);
        }

        var count1 = await dbContext.SaveChangesAsync();
    }
}