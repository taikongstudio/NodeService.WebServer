using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Services.FileRecords;

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
        [FromKeyedServices(nameof(FileRecordInsertUpdateDeleteService))]
        BatchQueue<BatchQueueOperation<FileRecordModel, bool>> cudBatchQueue
    )
    {
        _logger = logger;
        _exceptionCounter = exceptionCounter;
        _repositoryFactory = repositoryFactory;
        _cudBatchQueue = cudBatchQueue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var arrayPoolCollection in _cudBatchQueue.ReceiveAllAsync(stoppingToken))
                try
                {
                    if (arrayPoolCollection.CountNotDefault() == 0)
                    {
                        continue;
                    }
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
                                var modelFromRepo = await repo.FirstOrDefaultAsync(
                                    new FileRecordSpecification(
                                        operation.Argument.Id,
                                        null,
                                        operation.Argument.Name),
                                    stoppingToken);
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
                                    await repo.SaveChangesAsync(stoppingToken);
                                }
                                _logger.LogInformation($"Add or update SaveChanges:{repo.LastSaveChangesCount} {repo.LastSaveChangesTimeSpan}");

                                operation.SetResult(true);
                                break;
                            case BatchQueueOperationKind.Delete:
                                await repo.DeleteAsync(operation.Argument);
                                _logger.LogInformation($"Delete SaveChanges:{repo.LastSaveChangesCount} {repo.LastSaveChangesTimeSpan}");
                                operation.SetResult(true);
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
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
        }
    }
}