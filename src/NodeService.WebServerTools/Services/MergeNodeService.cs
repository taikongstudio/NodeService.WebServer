using CommandLine;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using NodeService.Infrastructure.Sessions;
using NodeService.WebServer.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServerTools.Services
{
    public class MergeNodeService : BackgroundService
    {
        private readonly ILogger<MergeNodeService> _logger;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;

        public MergeNodeService(
            ILogger<MergeNodeService> logger,
            IDbContextFactory<ApplicationDbContext> dbContextFactory)
        {
            _logger = logger;
            _dbContextFactory = dbContextFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var dbContext = this._dbContextFactory.CreateDbContext();
            var nodes = await dbContext.NodeInfoDbSet.ToListAsync();
            foreach (var group in nodes.GroupBy(x => x.Name))
            {
                var lastOnlineNode = group.OrderByDescending(x => x.Profile.ServerUpdateTimeUtc).FirstOrDefault();
                if (lastOnlineNode==null)
                {
                    continue;
                }
                foreach (var item in group.Where(x => x.Id != lastOnlineNode.Id)
                    .OrderByDescending(x => x.Profile.ServerUpdateTimeUtc))
                {
                    var nodeProfile = await dbContext.NodeProfilesDbSet.FirstOrDefaultAsync(x => x.Name == group.Key);
                    if (nodeProfile != null)
                    {
                        var oldNodeInfo = await dbContext.NodeInfoDbSet.FirstOrDefaultAsync(x => x.Id == nodeProfile.NodeInfoId);
                        if (oldNodeInfo != null)
                        {
                            _logger.LogInformation($"Remove:{oldNodeInfo.Id}");
                            dbContext.NodeInfoDbSet.Remove(oldNodeInfo);
                        }
                        nodeProfile.NodeInfoId = lastOnlineNode.Id;
                        lastOnlineNode.Profile = nodeProfile;
                        lastOnlineNode.ProfileId = lastOnlineNode.Profile.Id;
                    }
                    await dbContext.SaveChangesAsync();
                }
            }
        }
    }
}
