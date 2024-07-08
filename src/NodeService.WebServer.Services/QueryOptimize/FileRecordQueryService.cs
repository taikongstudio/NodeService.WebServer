using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.Models;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Services.QueryOptimize;

public class FileRecordQueryService : BackgroundService
{
    private readonly ExceptionCounter _exceptionCounter;
    private readonly ILogger<FileRecordQueryService> _logger;

    private readonly BatchQueue<
            BatchQueueOperation<FileRecordBatchQueryParameters,
                ListQueryResult<FileRecordModel>>>
        _queryBatchQueue;

    private readonly ApplicationRepositoryFactory<FileRecordModel> _repositoryFactory;

    public FileRecordQueryService(
        ExceptionCounter exceptionCounter,
        ILogger<FileRecordQueryService> logger,
        ApplicationRepositoryFactory<FileRecordModel> repositoryFactory,
        BatchQueue<BatchQueueOperation<FileRecordBatchQueryParameters,
            ListQueryResult<FileRecordModel>>> queryBatchQueue
    )
    {
        _exceptionCounter = exceptionCounter;
        _logger = logger;
        _repositoryFactory = repositoryFactory;
        _queryBatchQueue = queryBatchQueue;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await foreach (var arrayPoolCollection in _queryBatchQueue.ReceiveAllAsync(cancellationToken))
            try
            {
                using var repo = _repositoryFactory.CreateRepository();
                foreach (var argumentGroup in arrayPoolCollection
                             .Where(static x => x.Kind == BatchQueueOperationKind.Query)
                             .GroupBy(static x => x.Argument))
                {
                    var argument = argumentGroup.Key;
                    try
                    {
                        var queryResult = await QueryAsync(repo, argument);

                        foreach (var op in argumentGroup) op.SetResult(queryResult);
                    }
                    catch (Exception ex)
                    {
                        foreach (var op in argumentGroup) op.SetException(ex);
                        _exceptionCounter.AddOrUpdate(ex);
                        _logger.LogError(ex.ToString());
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

    private async Task<ListQueryResult<FileRecordModel>> QueryAsync(
        IRepository<FileRecordModel> repo,
        FileRecordBatchQueryParameters argument)
    {
        ListQueryResult<FileRecordModel> queryResult = default;
        try
        {
            var queryParameters = argument.QueryParameters;
            queryResult = await repo.PaginationQueryAsync(
                new FileRecordListSpecification(
                    queryParameters.Category,
                    queryParameters.State,
                    DataFilterCollection<string>.Includes(queryParameters.NodeIdList),
                    DataFilterCollection<string>.Includes(string.IsNullOrEmpty(queryParameters.Keywords)
                        ? []
                        : [queryParameters.Keywords]),
                    queryParameters.SortDescriptions),
                argument.PaginationInfo);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
        finally
        {
        }

        return queryResult;
    }
}