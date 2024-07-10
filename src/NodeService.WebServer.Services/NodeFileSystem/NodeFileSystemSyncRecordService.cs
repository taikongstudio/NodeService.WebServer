using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Data;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using OneOf;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.NodeFileSystem
{
    public record struct NodeFileSystemAddOrUpdateSyncRecordParameters
    {
        public NodeFileSystemAddOrUpdateSyncRecordParameters(NodeFileSyncRecordModel syncRecord)
        {
            SyncRecords = [syncRecord];
        }

        public NodeFileSystemAddOrUpdateSyncRecordParameters(IEnumerable<NodeFileSyncRecordModel> syncRecords)
        {
            SyncRecords = [.. syncRecords];
        }

        public NodeFileSystemAddOrUpdateSyncRecordParameters(ImmutableArray<NodeFileSyncRecordModel> syncRecords)
        {
            SyncRecords = syncRecords;
        }

        public IEnumerable<NodeFileSyncRecordModel> SyncRecords { get; private set; }
    }

    public record struct NodeFileSystemSyncRecordQueryParameters
    {
        public NodeFileSystemSyncRecordQueryParameters(QueryNodeFileSystemSyncRecordParameters parameters)
        {
            this.QueryParameters = parameters;
        }

        public QueryNodeFileSystemSyncRecordParameters QueryParameters { get; private set; }
    }

    public record struct NodeFileSystemSyncRecordServiceParameters
    {
        public NodeFileSystemSyncRecordServiceParameters(NodeFileSystemAddOrUpdateSyncRecordParameters parameters)
        {
            Parameters = parameters;
        }

        public NodeFileSystemSyncRecordServiceParameters(NodeFileSystemSyncRecordQueryParameters parameters)
        {
            Parameters = parameters;
        }

        public OneOf<
            NodeFileSystemAddOrUpdateSyncRecordParameters,
            NodeFileSystemSyncRecordQueryParameters
            > Parameters
        { get; private set; }
    }

    public record struct NodeFileSystemSyncRecordServiceAddOrUpdateResult
    {
        public int AddedCount { get; init; }

        public int ModifiedCount { get; init; }
    }

    public record struct NodeFileSystemSyncRecordServiceQueryResult
    {
        public NodeFileSystemSyncRecordServiceQueryResult(ListQueryResult<NodeFileSyncRecordModel> result)
        {
            Result = result;
        }

        public ListQueryResult<NodeFileSyncRecordModel> Result { get; private set; }
    }

    public record struct NodeFileSystemSyncRecordServiceResult
    {
        public NodeFileSystemSyncRecordServiceResult(NodeFileSystemSyncRecordServiceAddOrUpdateResult result)
        {
            Result = result;
        }

        public NodeFileSystemSyncRecordServiceResult(NodeFileSystemSyncRecordServiceQueryResult result)
        {
            Result = result;
        }

        public OneOf<NodeFileSystemSyncRecordServiceAddOrUpdateResult, NodeFileSystemSyncRecordServiceQueryResult> Result { get; private set; }
    }

    public class NodeFileSystemSyncRecordService : BackgroundService
    {
        private readonly ILogger<NodeFileSystemSyncRecordService> _logger;
        private readonly ExceptionCounter _exceptionCounter;
        readonly BatchQueue<BatchQueueOperation<NodeFileSystemSyncRecordServiceParameters, NodeFileSystemSyncRecordServiceResult>> _syncRecordQueue;
        readonly ApplicationRepositoryFactory<NodeFileSyncRecordModel> _syncRecordRepoFactory;

        public NodeFileSystemSyncRecordService(
            ILogger<NodeFileSystemSyncRecordService> logger,
            ExceptionCounter exceptionCounter,
            BatchQueue<BatchQueueOperation<NodeFileSystemSyncRecordServiceParameters, NodeFileSystemSyncRecordServiceResult>> nodeFileSyncRecordQueue,
                    ApplicationRepositoryFactory<NodeFileSyncRecordModel> nodeFileSyncRecordRepoFactory)
        {
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _syncRecordQueue = nodeFileSyncRecordQueue;
            _syncRecordRepoFactory = nodeFileSyncRecordRepoFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            await foreach (var array in _syncRecordQueue.ReceiveAllAsync(cancellationToken))
            {
                try
                {
                    if (array == null || array.Length == 0)
                    {
                        continue;
                    }
                    using var nodeFileSyncRecordRepo = _syncRecordRepoFactory.CreateRepository();
                    foreach (var op in array)
                    {
                        try
                        {
                            switch (op.Argument.Parameters.Index)
                            {
                                case 0:
                                    await ProcessAddOrUpdateParametersAsync(nodeFileSyncRecordRepo, op, cancellationToken);
                                    break;
                                case 1:
                                    await ProcessQueryAsync(nodeFileSyncRecordRepo, op, cancellationToken);
                                    break;
                                default:
                                    break;
                            }
                        }
                        catch (Exception ex)
                        {
                            _exceptionCounter.AddOrUpdate(ex);
                            _logger.LogError(ex.ToString());
                            op.SetException(ex);
                        }


                    }
                }
                catch (Exception ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                    foreach (var op in array)
                    {
                        op.SetException(ex);
                    }
                }


            }
        }

        static async ValueTask ProcessQueryAsync(IRepository<NodeFileSyncRecordModel> syncRecordRepo, BatchQueueOperation<NodeFileSystemSyncRecordServiceParameters, NodeFileSystemSyncRecordServiceResult> op, CancellationToken cancellationToken)
        {
            var queryParameters = op.Argument.Parameters.AsT1.QueryParameters;
            var nodeInfoIdFilters = DataFilterCollection<string>.Includes(queryParameters.NodeIdList);
            var syncRecordIdList = DataFilterCollection<string>.Includes(queryParameters.SyncRecordIdList);
            var specification = new NodeFileSystemSyncRecordSepecification(
                queryParameters.Status,
                nodeInfoIdFilters,
                syncRecordIdList,
                queryParameters.BeginDateTime,
                queryParameters.EndDateTime,
                queryParameters.SortDescriptions);
            var list = await syncRecordRepo.PaginationQueryAsync(
                specification,
                new PaginationInfo(queryParameters.PageIndex, queryParameters.PageSize),
                cancellationToken);
            var result = new NodeFileSystemSyncRecordServiceQueryResult(list);
            op.SetResult(new NodeFileSystemSyncRecordServiceResult(result));
        }

        static async ValueTask ProcessAddOrUpdateParametersAsync(IRepository<NodeFileSyncRecordModel> nodeFileSyncRecordRepo, BatchQueueOperation<NodeFileSystemSyncRecordServiceParameters, NodeFileSystemSyncRecordServiceResult> op, CancellationToken cancellationToken)
        {
            var syncRecords = op.Argument.Parameters.AsT0.SyncRecords;
            var addedCount = 0;
            var modifiedCount = 0;
            foreach (var syncRecordGroups in syncRecords.GroupBy(static x => x.Id))
            {
                var lastSyncRecord = syncRecordGroups.OrderBy(x => x.Status).ThenBy(x => x.Value.Progress).LastOrDefault();
                if (lastSyncRecord == null)
                {
                    continue;
                }
                var syncRecordFromDb = await nodeFileSyncRecordRepo.GetByIdAsync(lastSyncRecord.Id, cancellationToken);
                if (syncRecordFromDb == null)
                {
                    await nodeFileSyncRecordRepo.AddAsync(lastSyncRecord, cancellationToken);
                    addedCount++;
                }
                else
                {
                    syncRecordFromDb.With(lastSyncRecord);
                    await nodeFileSyncRecordRepo.UpdateAsync(syncRecordFromDb, cancellationToken);
                    modifiedCount++;
                }

            }
            var result = new NodeFileSystemSyncRecordServiceAddOrUpdateResult()
            {
                AddedCount = addedCount,
                ModifiedCount = modifiedCount
            };
            op.SetResult(new NodeFileSystemSyncRecordServiceResult(result));
        }
    }
}
