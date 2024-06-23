using Microsoft.EntityFrameworkCore;
using NodeService.Infrastructure.DataModels;
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

    //protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    //{
    //    await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
    //    var nodes = await dbContext.NodeInfoDbSet.ToListAsync();
    //    foreach (var group in nodes.GroupBy(x => x.Name))
    //    {
    //        var lastOnlineNode = group.OrderByDescending(x => x.Profile.ServerUpdateTimeUtc).FirstOrDefault();
    //        if (lastOnlineNode == null) continue;
    //        lastOnlineNode = await dbContext.NodeInfoDbSet.FindAsync(lastOnlineNode.Id);
    //        var count = 0;
    //        foreach (var node in group.Where(x => x.Id != lastOnlineNode.Id)
    //                     .OrderByDescending(x => x.Profile.ServerUpdateTimeUtc))
    //        {
    //            _logger.LogInformation($"Remove:{node.Id}");
    //            dbContext.NodeInfoDbSet.Remove(node);
    //            if (count > 1) continue;
    //            var profile = node.Profile;
    //            profile.NodeInfoId = lastOnlineNode.Id;
    //            lastOnlineNode.Profile = profile;
    //            lastOnlineNode.ProfileId = lastOnlineNode.Profile.Id;
    //            count++;
    //        }

    //        await dbContext.SaveChangesAsync();
    //    }
    //}

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (true)
            try
            {
                using var dbContext = _dbContextFactory.CreateDbContext();
                var pageIndex = 0;
                var pageSize = 10000;
                List<FileRecordModel> records = new();
                while (true)
                {
                    _logger.LogInformation($"Fetch page {pageIndex} ");
                    var dataList = await dbContext.FileRecordDbSet.Skip(pageIndex * pageSize).Take(pageSize)
                        .ToListAsync();
                    if (dataList.Count == 0) break;
                    records.AddRange(dataList);
                    pageIndex++;
                }

                try
                {
                    var groups = records.GroupBy(x => (x.Id, x.Name)).Where(x => x.Count() > 1).ToArray();
                    foreach (var group in groups)
                    {
                        _logger.LogInformation($"Processing {group.Key.Id} {group.Key.Name} {group.Count()}");

                        foreach (var item in group.OrderByDescending(x => x.EntityVersion).Skip(1))
                            try
                            {
                                var count = await dbContext.FileRecordDbSet
                                    .Where(x => x.Id == item.Id && x.Name == item.Name)
                                    .ExecuteDeleteAsync();
                                _logger.LogInformation($"Delete {item.Id} {item.Name} ");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex.ToString());
                            }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.ToString());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
            }
    }
}