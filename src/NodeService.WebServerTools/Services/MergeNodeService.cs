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
                if (lastOnlineNode == null)
                {
                    continue;
                }
                lastOnlineNode = await dbContext.NodeInfoDbSet.FindAsync(lastOnlineNode.Id);
                int count = 0;
                foreach (var node in group.Where(x => x.Id != lastOnlineNode.Id)
                    .OrderByDescending(x => x.Profile.ServerUpdateTimeUtc))
                {
                    _logger.LogInformation($"Remove:{node.Id}");
                    dbContext.NodeInfoDbSet.Remove(node);
                    if (count>1)
                    {
                        continue;
                    }
                    var profile = node.Profile;
                    profile.NodeInfoId = lastOnlineNode.Id;
                    lastOnlineNode.Profile = profile;
                    lastOnlineNode.ProfileId = lastOnlineNode.Profile.Id;
                    count++;
                }
                await dbContext.SaveChangesAsync();
            }
        }
    }
}
