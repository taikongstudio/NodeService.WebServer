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
        readonly BatchQueue<NodeFileSyncRecordModel> _batchBlock;
        readonly ActionBlock<NodeFileSyncRecordModel[]> _actionBlock;
        readonly IDisposable _token;

        public SyncRecordQueryService(
            ILogger<SyncRecordQueryService> logger,
            ExceptionCounter exceptionCounter,
            ApplicationRepositoryFactory<NodeFileSyncRecordModel> nodeFileSyncRecordRepoFactory)
        {
            _logger = logger;
            _exceptionCounter = exceptionCounter;
            _syncRecordRepoFactory = nodeFileSyncRecordRepoFactory;
            _batchBlock = new BatchQueue<NodeFileSyncRecordModel>(TimeSpan.FromSeconds(3), 10000);
            _actionBlock = new ActionBlock<NodeFileSyncRecordModel[]>(AddOrUpdateRangeAsync, new ExecutionDataflowBlockOptions()
            {
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1,
            });
            _token = _batchBlock.LinkTo(_actionBlock);

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
            await _batchBlock.SendAsync(syncRecord, cancellationToken);
        }

        async Task AddOrUpdateRangeAsync(NodeFileSyncRecordModel[] syncRecords)
        {
            try
            {
                if (syncRecords.Length == 0)
                {
                    return;
                }
                await using var syncRecordRepo = await _syncRecordRepoFactory.CreateRepositoryAsync();
                var groups = syncRecords.Distinct().GroupBy(static x => x.Id).ToArray();
                var idFilters = DataFilterCollection<string>.Includes(groups.Select(static x => x.Key));
                var modifiedList = await syncRecordRepo.ListAsync(new ListSpecification<NodeFileSyncRecordModel>(idFilters));
                if (modifiedList.Count == 0)
                {
                    await syncRecordRepo.AddRangeAsync(syncRecords);
                }
                else
                {
                    var addedItems = GetAddedItems(groups, modifiedList).ToArray();
                    if (addedItems.Length > 0)
                    {
                        await syncRecordRepo.AddRangeAsync(addedItems);
                    }
                    await syncRecordRepo.UpdateRangeAsync(modifiedList);
                }

            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }



        }

        static IEnumerable<NodeFileSyncRecordModel> GetAddedItems(IGrouping<string, NodeFileSyncRecordModel>[] groups, List<NodeFileSyncRecordModel> recordList)
        {
            foreach (var group in groups)
            {
                var lastSyncRecord = group.LastOrDefault();
                if (lastSyncRecord == null)
                {
                    continue;
                }
                NodeFileSyncRecordModel? syncRecordFromDb = null;
                foreach (var item in recordList)
                {
                    if (item.Id == lastSyncRecord.Id)
                    {
                        syncRecordFromDb = item;
                        break;
                    }
                }
                if (syncRecordFromDb == null)
                {
                    yield return lastSyncRecord;
                }
                else
                {
                    syncRecordFromDb.With(lastSyncRecord);
                }
            }
            yield break;
        }
    }
}
