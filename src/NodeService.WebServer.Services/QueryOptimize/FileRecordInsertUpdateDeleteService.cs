using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.QueryOptimize;

namespace NodeService.WebServer.Services.DataQuality;

public class FileRecordInsertUpdateDeleteService : BackgroundService
{
    private readonly BatchQueue<BatchQueueOperation<FileRecordModel, bool>> _cudBatchQueue;
    private readonly ExceptionCounter _exceptionCounter;
    private readonly ILogger<FileRecordQueryService> _logger;
    private readonly ApplicationRepositoryFactory<FileRecordModel> _repositoryFactory;

    public FileRecordInsertUpdateDeleteService(
        ILogger<FileRecordQueryService> logger,
        ExceptionCounter exceptionCounter,
        ApplicationRepositoryFactory<FileRecordModel> repositoryFactory,
        BatchQueue<BatchQueueOperation<FileRecordModel, bool>> cudBatchQueue
    )
    {
        _logger = logger;
        _exceptionCounter = exceptionCounter;
        _repositoryFactory = repositoryFactory;
        _cudBatchQueue = cudBatchQueue;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await foreach (var arrayPoolCollection in _cudBatchQueue.ReceiveAllAsync(cancellationToken))
            try
            {
                using var repo = _repositoryFactory.CreateRepository();
                foreach (var operation in arrayPoolCollection.Where(static x => x != null))
                {
                    if (operation == null) continue;
                    var kind = operation.Kind;
                    switch (kind)
                    {
                        case BatchQueueOperationKind.None:
                            break;
                        case BatchQueueOperationKind.InsertOrUpdate:
                            await InsertOrUpdateAsync(repo, operation, cancellationToken);

                            break;
                        case BatchQueueOperationKind.Delete:
                            await DeleteAsync(repo, operation);
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
            }
    }

    private async Task DeleteAsync(IRepository<FileRecordModel> repo,
        BatchQueueOperation<FileRecordModel, bool>? operation)
    {
        try
        {
            await repo.DeleteAsync(operation.Argument);
            _logger.LogInformation($"Delete SaveChanges:{repo.LastChangesCount} {repo.LastSaveChangesTimeSpan}");
            operation.SetResult(true);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
        finally
        {
            repo.DbContext.ChangeTracker.Clear();
        }
    }

    private async Task InsertOrUpdateAsync(IRepository<FileRecordModel> repo,
        BatchQueueOperation<FileRecordModel, bool>? operation, CancellationToken cancellationToken)
    {
        try
        {
            var modelFromRepo = await repo.FirstOrDefaultAsync(
                new FileRecordSpecification(
                    operation.Argument.Id,
                    null,
                    operation.Argument.Name),
                cancellationToken);
            if (modelFromRepo == null)
            {
                await repo.AddAsync(operation.Argument);
            }
            else
            {
                modelFromRepo.Category = operation.Argument.Category;
                modelFromRepo.State = operation.Argument.State;
                modelFromRepo.Size = operation.Argument.Size;
                modelFromRepo.Properties = operation.Argument.Properties;
                modelFromRepo.OriginalFileName = operation.Argument.OriginalFileName;
                modelFromRepo.CompressedSize = operation.Argument.CompressedSize;
                modelFromRepo.CompressedFileHashValue = operation.Argument.CompressedFileHashValue;
                modelFromRepo.FileHashValue = operation.Argument.FileHashValue;
                modelFromRepo.CreationDateTime = operation.Argument.CreationDateTime;
                modelFromRepo.ModifiedDateTime = operation.Argument.ModifiedDateTime;
                await repo.SaveChangesAsync(cancellationToken);
            }

            operation.SetResult(true);
        }
        catch (Exception ex)
        {
            operation.SetException(ex);
        }
        finally
        {
            repo.DbContext.ChangeTracker.Clear();
        }

        _logger.LogInformation($"Add or update SaveChanges:{repo.LastChangesCount} {repo.LastSaveChangesTimeSpan}");
    }
}