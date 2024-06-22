using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Counters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.NodeSessions;

public class NodeStatusChangeRecordService : BackgroundService
{
    private readonly ILogger<NodeStatusChangeRecordService> _logger;
    private readonly ExceptionCounter _exceptionCounter;
    private readonly ApplicationRepositoryFactory<NodeStatusChangeRecordModel> _recordRepoFactory;
    private readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepoFactory;
    private readonly BatchQueue<NodeStatusChangeRecordModel> _recordBatchQueue;
    private readonly WebServerCounter _webServerCounter;

    public NodeStatusChangeRecordService(
        ILogger<NodeStatusChangeRecordService> logger,
        ExceptionCounter exceptionCounter,
        ApplicationRepositoryFactory<NodeStatusChangeRecordModel> recordRepoFactory,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepoFactory,
        BatchQueue<NodeStatusChangeRecordModel> recordBatchQueue,
        WebServerCounter webServerCounter)
    {
        _logger = logger;
        _exceptionCounter = exceptionCounter;
        _recordRepoFactory = recordRepoFactory;
        _nodeInfoRepoFactory = nodeInfoRepoFactory;
        _recordBatchQueue = recordBatchQueue;
        _webServerCounter = webServerCounter;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await foreach (var arrayPoolCollection in _recordBatchQueue.ReceiveAllAsync(cancellationToken))
            try
            {
                using var recordRepo = _recordRepoFactory.CreateRepository();
                using var nodeInfoRepo = _nodeInfoRepoFactory.CreateRepository();
                recordRepo.DbContext.ChangeTracker.AutoDetectChangesEnabled = false;
                foreach (var recordGroup in arrayPoolCollection.GroupBy(static x => x.NodeId))
                {
                    var nodeId = recordGroup.Key;
                    var nodeInfo = await nodeInfoRepo.GetByIdAsync(nodeId, cancellationToken);
                    foreach (var record in recordGroup)
                        if (nodeInfo != null)
                            record.Name = nodeInfo.Name;
                        else
                            record.Name = "<Unknown>";
                }

                await recordRepo.AddRangeAsync(arrayPoolCollection, cancellationToken);
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
            finally
            {
            }
    }
}