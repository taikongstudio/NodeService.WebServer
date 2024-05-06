using Microsoft.EntityFrameworkCore;
using NodeService.WebServer.Data;

namespace NodeService.WebServerTools.Services;

public class MergeNodeService : BackgroundService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ILogger<MergeNodeService> _logger;

    public MergeNodeService(
        ILogger<MergeNodeService> logger,
        IDbContextFactory<ApplicationDbContext> dbContextFactory)
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var nodes = await dbContext.NodeInfoDbSet.ToListAsync();
        foreach (var group in nodes.GroupBy(x => x.Name))
        {
            var lastOnlineNode = group.OrderByDescending(x => x.Profile.ServerUpdateTimeUtc).FirstOrDefault();
            if (lastOnlineNode == null) continue;
            lastOnlineNode = await dbContext.NodeInfoDbSet.FindAsync(lastOnlineNode.Id);
            var count = 0;
            foreach (var node in group.Where(x => x.Id != lastOnlineNode.Id)
                         .OrderByDescending(x => x.Profile.ServerUpdateTimeUtc))
            {
                _logger.LogInformation($"Remove:{node.Id}");
                dbContext.NodeInfoDbSet.Remove(node);
                if (count > 1) continue;
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