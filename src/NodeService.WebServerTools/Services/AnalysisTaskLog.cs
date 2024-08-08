using Microsoft.EntityFrameworkCore;
using NodeService.Infrastructure.Models;
using NodeService.WebServer.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServerTools.Services
{
    internal class AnalysisTaskLogService : BackgroundService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ILogger<ClearConfigService> _logger;

        public AnalysisTaskLogService(
            ILogger<ClearConfigService> logger,
            IDbContextFactory<ApplicationDbContext> dbContextFactory)
        {
            _logger = logger;
            _dbContextFactory = dbContextFactory;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            HashSet<string> messages = new HashSet<string>();
            try
            {
                int pageIndex = 0;
                int pageSize = 1000;
                while (true)
                {
                    var list = await dbContext.TaskExecutionInstanceDbSet
                    .Where(x => x.Status == TaskExecutionStatus.Failed || x.Status == TaskExecutionStatus.Cancelled || x.Status == TaskExecutionStatus.PenddingTimeout)
                    .Skip(pageIndex * pageSize).Take(pageSize)
                    .ToListAsync();
                    foreach (var item in list)
                    {
                        if (item.Message == null)
                        {
                            continue;
                        }
                        if (item.Message.Contains("Exception", StringComparison.OrdinalIgnoreCase))
                        {
                            messages.Add($"{item.CreationDateTime} {item.Message}");
                        }
                        else if (item.Message.Contains("time", StringComparison.OrdinalIgnoreCase))
                        {
                            messages.Add($"{item.CreationDateTime} {item.Message}");
                        }
                    }
                    if (list.Count < pageSize)
                    {
                        break;
                    }
                    pageIndex++;
                }

                File.WriteAllLines("d:/test.txt", messages);
            }
            catch (Exception ex)
            {

            }

        }
    }
}
