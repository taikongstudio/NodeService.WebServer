using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.NodeFileSystem;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using OneOf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public record struct NodeFileSystemInfoIndexServiceParameters
    {
        public NodeFileSystemInfoIndexServiceParameters(QueryNodeFileSystemInfoParameters parameters)
        {
            this.Parameters = parameters;
        }

       public OneOf< QueryNodeFileSystemInfoParameters> Parameters { get; init; }
    }

    public record struct NodeFileSystemInfoIndexServiceResult
    {
        public NodeFileSystemInfoIndexServiceResult(ListQueryResult<NodeFileSystemInfoModel> result)
        {
            Result = result;
        }

        public ListQueryResult<NodeFileSystemInfoModel> Result { get; init; }
    }

    public record struct QueryNodeFileSystemInfoParameters
    {
        public QueryNodeFileSystemInfoParameters(List<string> value)
        {
            Value = value;
        }

        public OneOf<List<string>> Value { get; init; }
    }

    public class NodeFileSystemInfoEvent
    {
        public NodeFileSystemInfoEvent()
        {

        }

        public required string NodeFilePath { get; init; }

        public required string NodeFilePathHash { get; init; }

        public NodeFileInfo? ObjectInfo { get; init; }
    }
    public class NodeFileSystemObjectInfoIndexService : BackgroundService
    {
        readonly ILogger<NodeFileSystemObjectInfoIndexService> _logger;
        readonly ExceptionCounter _exceptionCounter;
        readonly BatchQueue<BatchQueueOperation<NodeFileSystemInfoEvent, bool>> _nodeFileSystemEventQueue;
        readonly BatchQueue<BatchQueueOperation<NodeFileSystemInfoIndexServiceParameters, NodeFileSystemInfoIndexServiceResult>> _nodeFileSystemInfoQueryQueue;
        readonly ApplicationRepositoryFactory<NodeFileSystemInfoModel> _nodeFileSystemInfoRepoFactory;
        readonly IDistributedCache _distributedCache;

        public NodeFileSystemObjectInfoIndexService(
            ILogger<NodeFileSystemObjectInfoIndexService> logger,
            ExceptionCounter exceptionCounter,
            BatchQueue<BatchQueueOperation<NodeFileSystemInfoEvent, bool>> nodeFileSystemEventQueue,
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
                if (Debugger.IsAttached)
                {
                    foreach (var op in array)
                    {
                        await ProcessOperationAsync(op, cancellationToken);
                    }
                }
                else
                {
                    await Parallel.ForEachAsync(array, new ParallelOptions()
                    {
                        MaxDegreeOfParallelism = 4,
                        CancellationToken = cancellationToken,
                    }, ProcessOperationAsync);
                }
            }
        }

        private async ValueTask ProcessOperationAsync(BatchQueueOperation<NodeFileSystemInfoIndexServiceParameters, NodeFileSystemInfoIndexServiceResult> op, CancellationToken cancellationToken = default)
        {
            try
            {
                using var nodeFileSystemInfoRepo = _nodeFileSystemInfoRepoFactory.CreateRepository();
                switch (op.Kind)
                {
                    case BatchQueueOperationKind.None:
                        break;
                    case BatchQueueOperationKind.AddOrUpdate:
                        break;
                    case BatchQueueOperationKind.Delete:
                        break;
                    case BatchQueueOperationKind.Query:
                        var queryParameters = op.Argument.Parameters.AsT0;
                        var list = await nodeFileSystemInfoRepo.ListAsync(
                            new NodeFileSystemInfoSpecification(
                                DataFilterCollection<string>.Includes(queryParameters.Value.AsT0)),
                            cancellationToken);
                        op.SetResult(new NodeFileSystemInfoIndexServiceResult(new ListQueryResult<NodeFileSystemInfoModel>(list.Count,
                            1,
                            list.Count,
                            list)));
                        break;
                    default:
                        break;
                }
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
                    using var nodeFileSystemWatchRecordRepo = _nodeFileSystemInfoRepoFactory.CreateRepository();
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
                        var record = await nodeFileSystemWatchRecordRepo.GetByIdAsync(nodeFilePathHash, cancellationToken);
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
                                        CreationDateTime = DateTime.UtcNow,
                                        Value = lastOp.Argument.ObjectInfo,
                                    };
                                    _distributedCache.SetString(nodeFilePath, JsonSerializer.Serialize(record.Value));
                                    await nodeFileSystemWatchRecordRepo.AddAsync(record, cancellationToken);
                                }
                                else
                                {
                                    record.Value = lastOp.Argument.ObjectInfo;
                                    _distributedCache.SetString(nodeFilePath, JsonSerializer.Serialize(record.Value));
                                    await nodeFileSystemWatchRecordRepo.UpdateAsync(record, cancellationToken);
                                }
                                break;
                            case BatchQueueOperationKind.Delete:
                                await nodeFileSystemWatchRecordRepo.DeleteAsync(record, cancellationToken);
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
