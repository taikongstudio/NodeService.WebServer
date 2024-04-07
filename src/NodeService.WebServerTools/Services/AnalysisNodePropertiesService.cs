using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using NodeService.Infrastructure.Models;
using NodeService.WebServer.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NodeService.WebServerTools.Services
{
    public class AnalysisNodePropertiesService : BackgroundService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;

        public AnalysisNodePropertiesService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var processUsageMappings = JsonSerializer.Deserialize<ProcessUsageMapping[]>(File.ReadAllText("mapping.json"));
            ApiResponse<int> apiResponse = new ApiResponse<int>();
            using var dbContext = _dbContextFactory.CreateDbContext();
            var nodeInfoList = await dbContext.NodeInfoDbSet.Include(x => x.Profile)
                //.Include(x => x.ConfigurationBindings)
                .ToArrayAsync();
            dbContext.NodeInfoDbSet.AttachRange(nodeInfoList);

            foreach (var nodeInfo in nodeInfoList)
            {
                string nodeId = nodeInfo.Id;
                var nodePropsSnapshotList =
                    await dbContext.NodePropertySnapshotsDbSet.FromSqlRaw($"select *\r\n" +
                    $"from NodePropertySnapshotsDbSet\r\nwhere NodeInfoId ='{nodeId}'\r\n limit {30}")
                    .ToArrayAsync();
                HashSet<string> usages = new HashSet<string>();
                foreach (var nodePropsSnapshot in nodePropsSnapshotList)
                {
                    var nodeProperpty = NodePropertyModel.FromNodePropertyItems(nodePropsSnapshot.NodeProperties);
                    foreach (var mapping in processUsageMappings)
                    {
                        if (nodeProperpty.Processes.Any(x => x.FileName.Contains(mapping.FileName)))
                        {
                            usages.Add(mapping.Name);
                        }
                    }
                }
                nodeInfo.Profile.Usages = usages.Count == 0 ? null : string.Join(",", usages.OrderBy(x => x));

            }

            var changesCount = await dbContext.SaveChangesAsync();
        }
    }
}
