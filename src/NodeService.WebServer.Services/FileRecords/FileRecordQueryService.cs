using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Data;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Models;

namespace NodeService.WebServer.Services.FileRecords
{
    public class FileRecordQueryService : BackgroundService
    {
        readonly ExceptionCounter _exceptionCounter;
        readonly ILogger<FileRecordQueryService> _logger;
        readonly ApplicationRepositoryFactory<FileRecordModel> _repositoryFactory;
        readonly BatchQueue<BatchQueueOperation<FileRecordSpecification, ListQueryResult<FileRecordModel>>> _queryBatchQueue;
        private readonly IRepository<FileRecordModel> _repo;

        public FileRecordQueryService(
        ExceptionCounter exceptionCounter,
        ILogger<FileRecordQueryService> logger,
        ApplicationRepositoryFactory<FileRecordModel> repositoryFactory,
        [FromKeyedServices(nameof(FileRecordQueryService))]
        BatchQueue<BatchQueueOperation<FileRecordSpecification, ListQueryResult<FileRecordModel>>> queryBatchQueue
            )
        {
            _exceptionCounter = exceptionCounter;
            _logger = logger;
            _repositoryFactory = repositoryFactory;
            _queryBatchQueue = queryBatchQueue;
            _repo = _repositoryFactory.CreateRepository();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await foreach (var arrayPoolCollection in _queryBatchQueue.ReceiveAllAsync(stoppingToken))
                {
                    try
                    {
                        foreach (var operation in arrayPoolCollection)
                        {
                            if (operation == null)
                            {
                                continue;
                            }
                            var kind = operation.Kind;
                            switch (kind)
                            {
                                case BatchQueueOperationKind.Query:
                                    await this._repo.PaginationQueryAsync(operation.Argument);
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
