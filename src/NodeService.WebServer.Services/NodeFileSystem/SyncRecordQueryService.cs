using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Data;
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

    public class SyncRecordQueryService 
    {
        readonly ILogger<SyncRecordQueryService> _logger;
        readonly ExceptionCounter _exceptionCounter;
        readonly ApplicationRepositoryFactory<NodeFileSyncRecordModel> _syncRecordRepoFactory;

        public SyncRecordQueryService(
            ILogger<SyncRecordQueryService> logger,
            ExceptionCounter exceptionCounter,
            ApplicationRepositoryFactory<NodeFileSyncRecordModel> nodeFileSyncRecordRepoFactory)
        {
            _logger = logger;
            _exceptionCounter = exceptionCounter;

            _syncRecordRepoFactory = nodeFileSyncRecordRepoFactory;
        }

        public async ValueTask<ListQueryResult<NodeFileSyncRecordModel>> QueryAsync(
            QueryNodeFileSystemSyncRecordParameters queryParameters,
            CancellationToken cancellationToken = default)
        {
            await using var syncRecordRepo = await _syncRecordRepoFactory.CreateRepositoryAsync(cancellationToken);
            var nodeInfoIdFilters = DataFilterCollection<string>.Includes(queryParameters.NodeIdList);
            var syncRecordIdList = DataFilterCollection<string>.Includes(queryParameters.SyncRecordIdList);
            var specification = new NodeFileSystemSyncRecordSepecification(
                queryParameters.Status,
                nodeInfoIdFilters,
                syncRecordIdList,
                queryParameters.BeginDateTime,
                queryParameters.EndDateTime,
                queryParameters.SortDescriptions);
            var result = await syncRecordRepo.PaginationQueryAsync(
                specification,
                new PaginationInfo(queryParameters.PageIndex, queryParameters.PageSize),
                cancellationToken);
            return result;
        }

        public async ValueTask AddOrUpdateAsync(
            NodeFileSyncRecordModel syncRecord,
            CancellationToken cancellationToken = default)
        {
            await using var syncRecordRepo = await _syncRecordRepoFactory.CreateRepositoryAsync(cancellationToken);
            var syncRecordFromDb = await syncRecordRepo.GetByIdAsync(syncRecord.Id, cancellationToken);
            if (syncRecordFromDb == null)
            {
                await syncRecordRepo.AddAsync(syncRecord, cancellationToken);
            }
            else
            {
                syncRecordFromDb.With(syncRecord);
                await syncRecordRepo.UpdateAsync(syncRecordFromDb, cancellationToken);
            }
        }

    }
}
