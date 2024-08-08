using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.DataServices;

namespace NodeService.WebServer.Services.NodeSessions;

public class NodeStatusChangeRecordService : BackgroundService
{
    private readonly ILogger<NodeStatusChangeRecordService> _logger;
    private readonly ExceptionCounter _exceptionCounter;
    private readonly ApplicationRepositoryFactory<NodeStatusChangeRecordModel> _recordRepoFactory;
    private readonly NodeInfoQueryService _nodeInfoQueryService;
    private readonly BatchQueue<NodeStatusChangeRecordModel> _recordBatchQueue;
    private readonly WebServerCounter _webServerCounter;

    public NodeStatusChangeRecordService(
        ILogger<NodeStatusChangeRecordService> logger,
        ExceptionCounter exceptionCounter,
        ApplicationRepositoryFactory<NodeStatusChangeRecordModel> recordRepoFactory,
        NodeInfoQueryService  nodeInfoQueryService,
        BatchQueue<NodeStatusChangeRecordModel> recordBatchQueue,
        WebServerCounter webServerCounter)
    {
        _logger = logger;
        _exceptionCounter = exceptionCounter;
        _recordRepoFactory = recordRepoFactory;
        _nodeInfoQueryService = nodeInfoQueryService;
        _recordBatchQueue = recordBatchQueue;
        _webServerCounter = webServerCounter;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await foreach (var array in _recordBatchQueue.ReceiveAllAsync(cancellationToken))
            try
            {
                if (array == null || array.Length == 0)
                {
                    continue;
                }
                await using var recordRepo = await _recordRepoFactory.CreateRepositoryAsync();
                recordRepo.DbContext.ChangeTracker.AutoDetectChangesEnabled = false;
                foreach (var recordGroup in array.GroupBy(static x => x.NodeId))
                {
                    var nodeId = recordGroup.Key;
                    var nodeInfo = await _nodeInfoQueryService.QueryNodeInfoByIdAsync(nodeId, true, cancellationToken);
                    foreach (var record in recordGroup)
                        if (nodeInfo != null)
                            record.Name = nodeInfo.Name;
                        else
                            record.Name = "<Unknown>";
                }

                await recordRepo.AddRangeAsync(array, cancellationToken);
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