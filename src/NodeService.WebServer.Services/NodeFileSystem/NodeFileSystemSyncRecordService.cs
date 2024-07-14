using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.NodeFileSystem
{

    public class NodeFileSystemSyncRecordService : BackgroundService
    {
        readonly ILogger<NodeFileSystemSyncRecordService> _logger;
        readonly ExceptionCounter _exceptionCounter;
        readonly BatchQueue<AsyncOperation<SyncRecordServiceParameters, SyncRecordServiceResult>> _syncRecordQueue;
        readonly ApplicationRepositoryFactory<NodeFileSyncRecordModel> _syncRecordRepoFactory;

        public NodeFileSystemSyncRecordService(
            ILogger<NodeFileSystemSyncRecordService> logger,
            ExceptionCounter exceptionCounter,
            BatchQueue<AsyncOperation<SyncRecordServiceParameters, SyncRecordServiceResult>> nodeFileSyncRecordQueue,
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
                            op.TrySetException(ex);
                        }


                    }
                }
                catch (Exception ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                    foreach (var op in array)
                    {
                        op.TrySetException(ex);
                    }
                }


            }
        }

        static async ValueTask ProcessQueryAsync(IRepository<NodeFileSyncRecordModel> syncRecordRepo, AsyncOperation<SyncRecordServiceParameters, SyncRecordServiceResult> op, CancellationToken cancellationToken)
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
            var result = new SyncRecordServiceQueryResult(list);
            op.TrySetResult(new SyncRecordServiceResult(result));
        }

        static async ValueTask ProcessAddOrUpdateParametersAsync(IRepository<NodeFileSyncRecordModel> nodeFileSyncRecordRepo, AsyncOperation<SyncRecordServiceParameters, SyncRecordServiceResult> op, CancellationToken cancellationToken)
        {
            var syncRecords = op.Argument.Parameters.AsT0.SyncRecords;
            var addedCount = 0;
            var modifiedCount = 0;
            foreach (var syncRecordGroups in syncRecords.GroupBy(static x => x.Id))
            {
                var id = syncRecordGroups.Key;
                var syncRecordFromDb = await nodeFileSyncRecordRepo.GetByIdAsync(id, cancellationToken);
                foreach (var record in syncRecordGroups)
                {
                    if (syncRecordFromDb == null)
                    {
                        syncRecordFromDb = record;
                        addedCount++;
                    }
                    else
                    {
                        syncRecordFromDb.With(record);
                        modifiedCount++;
                    }
                }

                if (syncRecordFromDb != null)
                {
                    if (addedCount > 0)
                    {
                        await nodeFileSyncRecordRepo.AddAsync(syncRecordFromDb, cancellationToken);
                    }
                    if (modifiedCount > 0)
                    {
                        await nodeFileSyncRecordRepo.UpdateAsync(syncRecordFromDb, cancellationToken);
                    }
                }

            }
            var result = new SyncRecordServiceAddOrUpdateResult()
            {
                AddedCount = addedCount,
                ModifiedCount = modifiedCount
            };
            op.TrySetResult(new SyncRecordServiceResult(result));
        }
    }
}
