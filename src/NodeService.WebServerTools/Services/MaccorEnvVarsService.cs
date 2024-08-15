using Microsoft.EntityFrameworkCore;
using NodeService.WebServer.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServerTools.Services
{
    internal class MaccorEnvVarsService : BackgroundService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ILogger<MaccorEnvVarsService> _logger;

        public MaccorEnvVarsService(
            ILogger<MaccorEnvVarsService> logger,
            IDbContextFactory<ApplicationDbContext> dbContextFactory)
        {
            _logger = logger;
            _dbContextFactory = dbContextFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var maccorNodes = await dbContext.NodeInfoDbSet.Where(x => x.Profile.Usages.Contains("maccor", StringComparison.OrdinalIgnoreCase)).ToListAsync();
            foreach (var item in maccorNodes)
            {
                var config = await dbContext.NodeEnvVarsConfigurationDbSet.FindAsync(new object[] { item.Id });
                if (config == null)
                {
                    config = new Infrastructure.DataModels.NodeEnvVarsConfigModel()
                    {
                        Id = item.Id,
                        Name = item.Name,

                    };
                }
                var maccorDirEntry = config.Value.EnvironmentVariables.FirstOrDefault(x => x.Name == "MACCOR_DIR");
                if (maccorDirEntry == null)
                {
                    maccorDirEntry = new Infrastructure.DataModels.StringEntry()
                    {
                        Name = "MACCOR_DIR",
                        Value = "D:\\Maccor"
                    };
                    config.Value.EnvironmentVariables.Add(maccorDirEntry);
                }
                dbContext.NodeEnvVarsConfigurationDbSet.Add(config);
            }
            await dbContext.SaveChangesAsync();
        }

    }
}
