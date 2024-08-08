using CommandLine;
using Microsoft.EntityFrameworkCore;
using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Models;
using NodeService.WebServer.Data;
using NodeService.WebServer.Services.DataQueue;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NodeService.WebServerTools.Services
{
    internal class UpdateOfflineNodeService : BackgroundService
    {

        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly IDbContextFactory<LimsDbContext> _limsDbContextFactory;
        private readonly NodeInfoQueryService _nodeInfoQueryService;
        private readonly ILogger<ClearConfigService> _logger;

        public UpdateOfflineNodeService(
            ILogger<ClearConfigService> logger,
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            IDbContextFactory<LimsDbContext> limsDbContextFactory,
            NodeInfoQueryService nodeInfoQueryService)
        {
            _logger = logger;
            _dbContextFactory = dbContextFactory;
            _limsDbContextFactory = limsDbContextFactory;
            _nodeInfoQueryService = nodeInfoQueryService;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            int pageIndex = 0;
            int pageSize = 1000;
            using var dbContext = _dbContextFactory.CreateDbContext();
            using var limsDbContext = _limsDbContextFactory.CreateDbContext();


            while (true)
            {
                var list = await dbContext.NodeInfoDbSet.Where(x => x.Status == NodeStatus.Offline).ToListAsync();
                foreach (var nodeInfo in list)
                {
                    var computerInfo = await _nodeInfoQueryService.Query_dl_equipment_ctrl_computer_Async(
                    nodeInfo.Id,
                        nodeInfo.Profile.LimsDataId,
                        cancellationToken);

                    if (computerInfo == null)
                    {
                        nodeInfo.Profile.FoundInLims = false;
                    }
                    else
                    {
                        if (nodeInfo.Profile.LimsDataId == null)
                        {
                            nodeInfo.Profile.LimsDataId = computerInfo.id;
                        }
                        nodeInfo.Profile.FoundInLims = true;
                        nodeInfo.Profile.LabArea = computerInfo.LabArea?.name;
                        nodeInfo.Profile.LabName = computerInfo.LabInfo?.name;
                        nodeInfo.Profile.Manager = computerInfo.manager_name;
                    }
                }
                if (list.Count < pageSize)
                {
                    break;
                }
                pageIndex++;
            }

            await dbContext.SaveChangesAsync();

        }
    }
}
