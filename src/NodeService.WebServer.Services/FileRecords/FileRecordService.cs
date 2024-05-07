using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Messages;
using NodeService.Infrastructure.Models;
using NodeService.Infrastructure.Sessions;
using NodeService.WebServer.Data;
using NodeService.WebServer.Extensions;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.NodeSessions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.FileRecords
{
    public class FileRecordService : BackgroundService
    {
        private readonly ExceptionCounter _exceptionCounter;
        private readonly ILogger<FileRecordService> _logger;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly BatchQueue<BatchQueueOperation<object, object>> _fileRecordsBatchQueue;

        public FileRecordService(
        ExceptionCounter exceptionCounter,
        ILogger<FileRecordService> logger,
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        [FromKeyedServices(nameof(FileRecordService))] BatchQueue<BatchQueueOperation<object, object>> fileRecordsBatchQueue
            )
        {
            _exceptionCounter = exceptionCounter;
            _logger = logger;
            _dbContextFactory = dbContextFactory;
            _fileRecordsBatchQueue = fileRecordsBatchQueue;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var arrayPoolCollection in _fileRecordsBatchQueue.ReceiveAllAsync(stoppingToken))
            {
                try
                {
                    await using var dbContext = await _dbContextFactory.CreateDbContextAsync(stoppingToken);
                    foreach (var operationGroup in arrayPoolCollection.Where(static x=>x!=null).GroupBy(static x=>x.Kind))
                    {
                        if (operationGroup == null)
                        {
                            continue;
                        }
                        var kind = operationGroup.Key;
                        switch (kind)
                        {
                            case BatchQueueOperationKind.None:
                                break;
                            case BatchQueueOperationKind.InsertOrUpdate:
                                foreach (var operation in operationGroup)
                                {
                                    if (operation==null)
                                    {
                                        continue;
                                    }
                                    await InsertOrUpdateAsync(dbContext, operation.Argument as FileRecordModel, stoppingToken);
                                }
                                int count = await dbContext.SaveChangesAsync(stoppingToken);
                                foreach (var operation in operationGroup)
                                {
                                    if (operation == null)
                                    {
                                        continue;
                                    }
                                    operation.SetResult(null);
                                }
                                _logger.LogInformation($"SaveChanges:{count}");
                                dbContext.ChangeTracker.Clear();
                                break;
                            case BatchQueueOperationKind.Delete:
                                break;
                            case BatchQueueOperationKind.Query:
                                foreach (var operation in operationGroup)
                                {
                                    if (operation == null)
                                    {
                                        continue;
                                    }
                                    var rsp = await QueryAsync(dbContext, operation);
                                    operation.SetResult(rsp);
                                }
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
                finally
                {
                    arrayPoolCollection.Dispose();
                }
            }
        }

        private async Task<PaginationResponse<FileRecordModel>> QueryAsync(
            ApplicationDbContext dbContext,
            BatchQueueOperation<object, object> operation)
        {
            var apiResponse = new PaginationResponse<FileRecordModel>();
            try
            {
                var parameters = operation.Argument as Tuple<string, PaginationQueryParameters>;
                string nodeId = parameters.Item1;
                if (string.IsNullOrEmpty(parameters.Item2.Keywords))
                {

                    IQueryable<FileRecordModel> queryable = dbContext.FileRecordsDbSet.Where(x => x.Id == nodeId)
                        .OrderBy(x => x.ModifyDateTime);

                    apiResponse = await queryable.QueryPageItemsAsync(parameters.Item2);
                }
                else
                {
                    apiResponse.SetResult([await dbContext.FileRecordsDbSet.FindAsync(
                    nodeId,
                    parameters.Item2.Keywords)]);
                    apiResponse.SetTotalCount(1);
                    apiResponse.SetPageIndex(parameters.Item2.PageIndex);
                    apiResponse.SetPageSize(parameters.Item2.PageSize);

                }
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
            finally
            {
                dbContext.ChangeTracker.Clear();
            }

            return apiResponse;
        }

        private async Task InsertOrUpdateAsync(ApplicationDbContext dbContext, FileRecordModel model, CancellationToken stoppingToken)
        {
            var fileRecordFromDb = await dbContext.FileRecordsDbSet.FindAsync(model.Id, model.Name);
            if (fileRecordFromDb == null)
            {
                await dbContext.FileRecordsDbSet.AddAsync(model, stoppingToken);
            }
            else
            {
                fileRecordFromDb.ModifyDateTime = model.ModifyDateTime;
                if (model.Properties != null) fileRecordFromDb.Properties = model.Properties;
                if (model.FileHashValue != null) fileRecordFromDb.FileHashValue = model.FileHashValue;
                if (model.Size > 0) fileRecordFromDb.Size = model.Size;
                if (model.OriginalFileName != null) fileRecordFromDb.OriginalFileName = model.OriginalFileName;
                if (model.State != FileRecordState.None) fileRecordFromDb.State = model.State;
                if (model.CompressedSize > 0) fileRecordFromDb.CompressedSize = model.CompressedSize;
                if (model.CompressedFileHashValue != null)
                    fileRecordFromDb.CompressedFileHashValue = model.CompressedFileHashValue;
            }
        }
    }
}
