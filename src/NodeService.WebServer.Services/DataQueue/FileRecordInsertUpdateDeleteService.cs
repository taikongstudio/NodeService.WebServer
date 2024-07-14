using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Services.DataQueue;


public record class FileRecordBatchQueryParameters
{
    public FileRecordBatchQueryParameters(
        QueryFileRecordListParameters queryParameters,
        PaginationInfo paginationInfo)
    {
        QueryParameters = queryParameters;
        PaginationInfo = paginationInfo;
    }

    public QueryFileRecordListParameters QueryParameters { get; private set; }

    public PaginationInfo PaginationInfo { get; private set; }
}

public class FileRecordInsertUpdateDeleteService : BackgroundService
{
    private readonly BatchQueue<AsyncOperation<FileRecordModel, bool>> _cudBatchQueue;
    private readonly ExceptionCounter _exceptionCounter;
    private readonly ILogger<FileRecordQueryService> _logger;
    private readonly ApplicationRepositoryFactory<FileRecordModel> _repositoryFactory;

    public FileRecordInsertUpdateDeleteService(
        ILogger<FileRecordQueryService> logger,
        ExceptionCounter exceptionCounter,
        ApplicationRepositoryFactory<FileRecordModel> repositoryFactory,
        BatchQueue<AsyncOperation<FileRecordModel, bool>> cudBatchQueue
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
                        case AsyncOperationKind.None:
                            break;
                        case AsyncOperationKind.AddOrUpdate:
                            await InsertOrUpdateAsync(repo, operation, cancellationToken);

                            break;
                        case AsyncOperationKind.Delete:
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
        AsyncOperation<FileRecordModel, bool>? operation)
    {
        try
        {
            await repo.DeleteAsync(operation.Argument);
            _logger.LogInformation($"Delete SaveChanges:{repo.LastSaveChangesCount} {repo.LastOperationTimeSpan}");
            operation.TrySetResult(true);
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
        AsyncOperation<FileRecordModel, bool>? operation, CancellationToken cancellationToken)
    {
        try
        {
            var modelFromRepo = await repo.FirstOrDefaultAsync(
                new FileRecordListSpecification(
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

                await repo.SaveChangesAsync(cancellationToken);
            }

            operation.TrySetResult(true);
        }
        catch (Exception ex)
        {
            operation.TrySetException(ex);
        }
        finally
        {
            repo.DbContext.ChangeTracker.Clear();
        }

        _logger.LogInformation($"Add or update SaveChanges:{repo.LastSaveChangesCount} {repo.LastOperationTimeSpan}");
    }
}