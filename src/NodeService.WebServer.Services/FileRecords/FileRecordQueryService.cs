using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Data;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Services.FileRecords;

public class FileRecordQueryService : BackgroundService
{
    private readonly ExceptionCounter _exceptionCounter;
    private readonly ILogger<FileRecordQueryService> _logger;

    private readonly BatchQueue<
        BatchQueueOperation<(
            FileRecordSpecification Specification,
            PaginationInfo PaginationInfo),
            ListQueryResult<FileRecordModel>>>
        _queryBatchQueue;

    private readonly IRepository<FileRecordModel> _repo;
    private readonly ApplicationRepositoryFactory<FileRecordModel> _repositoryFactory;

    public FileRecordQueryService(
        ExceptionCounter exceptionCounter,
        ILogger<FileRecordQueryService> logger,
        ApplicationRepositoryFactory<FileRecordModel> repositoryFactory,
        [FromKeyedServices(nameof(FileRecordQueryService))]
        BatchQueue<BatchQueueOperation<(
            FileRecordSpecification Specification,
            PaginationInfo PaginationInfo),
            ListQueryResult<FileRecordModel>>> queryBatchQueue
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
                    foreach (var operation in arrayPoolCollection.Where(static x => x != null))
                    {
                        var kind = operation.Kind;
                        switch (kind)
                        {
                            case BatchQueueOperationKind.Query:
                                try
                                {
                                    var queryResult = await _repo.PaginationQueryAsync(
                                        operation.Argument.Specification,
                                        operation.Argument.PaginationInfo);
                                    operation.SetResult(queryResult);
                                }
                                catch (Exception ex)
                                {
                                    _exceptionCounter.AddOrUpdate(ex);
                                    _logger.LogError(ex.ToString());
                                }
                                finally
                                {

                                }

                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
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