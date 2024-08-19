using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using NodeService.WebServer.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServerTools.Services
{
    public class DeleteSkippedSyncRecordService : BackgroundService
    {
        readonly ILogger<DeleteSkippedSyncRecordService> _logger;
        readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;

        public DeleteSkippedSyncRecordService(
            ILogger<DeleteSkippedSyncRecordService> logger,
            IDbContextFactory<ApplicationDbContext> dbContextFactory)
        {
            _logger = logger;
            _dbContextFactory = dbContextFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            for (int i = 1; i < 52; i++)
            {
                var count = await dbContext.NodeFileSyncRecordDbSet.Where(x => x.Status == Infrastructure.NodeFileSystem.NodeFileSyncStatus.Skipped
                && x.CreationDateTime >= DateTime.Now.AddDays(-7 * i))
                        .ExecuteDeleteAsync(cancellationToken: cancellationToken);
                _logger.LogInformation(count.ToString());
            }
        }

    }
}
