using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public class NodeFileSystemInfoIndexService : BackgroundService
    {
        readonly ILogger<NodeFileSystemInfoIndexService> _logger;
        readonly ExceptionCounter _exceptionCounter;
        readonly BatchQueue<BatchQueueOperation<NodeFileSystemWatchEvent, bool>> _nodeFileSystemEventQueue;
        readonly BatchQueue<BatchQueueOperation<NodeFileSystemInfoIndexServiceParameters, NodeFileSystemInfoIndexServiceResult>> _nodeFileSystemInfoQueryQueue;
        readonly ApplicationRepositoryFactory<NodeFileSystemInfoModel> _nodeFileSystemInfoRepoFactory;
        readonly IDistributedCache _distributedCache;

        public NodeFileSystemInfoIndexService(
            ILogger<NodeFileSystemInfoIndexService> logger,
            ExceptionCounter exceptionCounter,
            BatchQueue<BatchQueueOperation<NodeFileSystemWatchEvent, bool>> nodeFileSystemEventQueue,
            BatchQueue<BatchQueueOperation<NodeFileSystemInfoIndexServiceParameters, NodeFileSystemInfoIndexServiceResult>> queryQueue,
            ApplicationRepositoryFactory<NodeFileSystemInfoModel> nodeFileSystemWatchRecordModelRepoFactory,
            IDistributedCache distributedCache)
        {
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _nodeFileSystemEventQueue = nodeFileSystemEventQueue;
            _nodeFileSystemInfoQueryQueue = queryQueue;
            _nodeFileSystemInfoRepoFactory = nodeFileSystemWatchRecordModelRepoFactory;
            _distributedCache = distributedCache;
        }


        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            await Task.WhenAll(
                ConsumeNodeFileSystemInfoEventAsync(cancellationToken),
                ConsumeBatchOperationAsync(cancellationToken));
        }

        private async Task ConsumeBatchOperationAsync(CancellationToken cancellationToken)
        {
            await foreach (var array in _nodeFileSystemInfoQueryQueue.ReceiveAllAsync(cancellationToken))
            {
                if (array == null || array.Length == 0)
                {
                    continue;
                }
                try
                {
                    using var nodeFileSystemInfoRepo = _nodeFileSystemInfoRepoFactory.CreateRepository();
                    foreach (var op in array)
                    {
                        switch (op.Kind)
                        {
                            case BatchQueueOperationKind.None:
                                break;
                            case BatchQueueOperationKind.AddOrUpdate:

                                break;
                            case BatchQueueOperationKind.Delete:
                                break;
                            case BatchQueueOperationKind.Query:
                                await ProcessQueryOperationAsync(
                                                nodeFileSystemInfoRepo,
                                                op,
                                                cancellationToken);
                                break;
                            default:
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    foreach (var op in array)
                    {
                        op.SetException(ex);
                    }
                }

            }
        }

        async ValueTask ProcessQueryOperationAsync(
            IRepository<NodeFileSystemInfoModel> repository,
            BatchQueueOperation<NodeFileSystemInfoIndexServiceParameters, NodeFileSystemInfoIndexServiceResult> op, CancellationToken cancellationToken = default)
        {
            try
            {

                var queryParameters = op.Argument.Parameters.AsT0.Value.AsT0;
                var idList = new List<string>();
                foreach (var item in queryParameters.FilePathList)
                {
                    var nodeFilePath = NodeFileSystemHelper.GetNodeFilePath(queryParameters.NodeInfoId, item);
                    var nodeFilePathHash = NodeFileSystemHelper.GetNodeFilePathHash(nodeFilePath);
                    idList.Add(nodeFilePathHash);
                }
                var list = await repository.PaginationQueryAsync(
                                                        new NodeFileSystemInfoSpecification(
                                                            queryParameters.NodeInfoId,
                                                            DataFilterCollection<string>.Includes(idList)),
                                                            new PaginationInfo(queryParameters.PageIndex, queryParameters.PageSize),
                                                        cancellationToken);
                op.SetResult(new NodeFileSystemInfoIndexServiceResult(list));
            }
            catch (Exception ex)
            {
                op.SetException(ex);
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }

        }

        async Task ConsumeNodeFileSystemInfoEventAsync(CancellationToken cancellationToken = default)
        {
            await foreach (var array in _nodeFileSystemEventQueue.ReceiveAllAsync(cancellationToken))
            {
                try
                {
                    using var fileSystemInfoRepo = _nodeFileSystemInfoRepoFactory.CreateRepository();
                    foreach (var opGroup in array.GroupBy(x => x.Argument.NodeFilePathHash))
                    {
                        if (opGroup.Key == null)
                        {
                            continue;
                        }
                        var lastOp = opGroup.LastOrDefault();
                        if (lastOp == null)
                        {
                            continue;
                        }
                        var nodeFilePath = lastOp.Argument.NodeFilePath;
                        var nodeFilePathHash = lastOp.Argument.NodeFilePathHash;
                        var record = await fileSystemInfoRepo.GetByIdAsync(nodeFilePathHash, cancellationToken);
                        switch (lastOp.Kind)
                        {
                            case BatchQueueOperationKind.None:
                                break;
                            case BatchQueueOperationKind.AddOrUpdate:
                                if (record == null)
                                {
                                    record = new NodeFileSystemInfoModel()
                                    {
                                        Id = lastOp.Argument.NodeFilePathHash,
                                        Value = lastOp.Argument.ObjectInfo,
                                        Path = lastOp.Argument.NodeFilePath,
                                        Name = lastOp.Argument.ObjectInfo.FullName,
                                    };
                                    _distributedCache.SetString(nodeFilePath, JsonSerializer.Serialize(record.Value));
                                    await fileSystemInfoRepo.AddAsync(record, cancellationToken);
                                }
                                else
                                {
                                    record.Value = lastOp.Argument.ObjectInfo;
                                    _distributedCache.SetString(nodeFilePath, JsonSerializer.Serialize(record.Value));
                                    await fileSystemInfoRepo.UpdateAsync(record, cancellationToken);
                                }
                                break;
                            case BatchQueueOperationKind.Delete:
                                await fileSystemInfoRepo.DeleteAsync(record, cancellationToken);
                                _distributedCache.Remove(nodeFilePath);
                                break;
                            case BatchQueueOperationKind.Query:
                                break;
                            default:
                                break;
                        }
                    }

                }
                catch (Exception ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }

            }
        }
    }
}
