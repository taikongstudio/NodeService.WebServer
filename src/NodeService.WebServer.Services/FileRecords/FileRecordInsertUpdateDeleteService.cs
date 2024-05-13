using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Models;

namespace NodeService.WebServer.Services.FileRecords
{
    public class FileRecordInsertUpdateDeleteService : BackgroundService,IDisposable
    {
        readonly ExceptionCounter _exceptionCounter;
        readonly ILogger<FileRecordQueryService> _logger;
        readonly ApplicationRepositoryFactory<FileRecordModel> _repositoryFactory;
        readonly BatchQueue<BatchQueueOperation<FileRecordModel, bool>> _cudBatchQueue;
        private readonly IRepository<FileRecordModel> _repo;

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
            _repo = _repositoryFactory.CreateRepository();
        }

        public override void Dispose()
        {
            _repo.Dispose();
            base.Dispose();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await foreach (var arrayPoolCollection in _cudBatchQueue.ReceiveAllAsync(stoppingToken))
                {
                    try
                    {
                        foreach (var operation in arrayPoolCollection.Where(static x => x != null))
                        {
                            if (operation == null)
                            {
                                continue;
                            }
                            var kind = operation.Kind;
                            switch (kind)
                            {
                                case BatchQueueOperationKind.None:
                                    break;
                                case BatchQueueOperationKind.InsertOrUpdate:
                                    var modelFromDb = await _repo.GetByIdAsync((operation.Argument.Id, operation.Argument.Name));
                                    if (modelFromDb == null)
                                    {
                                        await _repo.AddAsync(operation.Argument);
                                    }
                                    else
                                    {
                                        modelFromDb.Category = operation.Argument.Category;
                                        modelFromDb.State = operation.Argument.State;
                                        modelFromDb.Size = operation.Argument.Size;
                                        modelFromDb.Properties = operation.Argument.Properties;
                                        modelFromDb.OriginalFileName = operation.Argument.OriginalFileName;
                                        modelFromDb.CompressedSize = operation.Argument.CompressedSize;
                                        modelFromDb.CompressedFileHashValue = operation.Argument.CompressedFileHashValue;
                                        modelFromDb.FileHashValue = operation.Argument.FileHashValue;
                                        await _repo.SaveChangesAsync(stoppingToken);
                                    }

                                    _logger.LogInformation($"Add or update SaveChanges:{_repo.LastSaveChangesCount}");

                                    operation.SetResult(true);
                                    break;
                                case BatchQueueOperationKind.Delete:
                                    await _repo.DeleteAsync(operation.Argument);
                                    _logger.LogInformation($"Delete SaveChanges:{_repo.LastSaveChangesCount}");
                                    operation.SetResult(true);
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
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
            }

        }

    }
}
