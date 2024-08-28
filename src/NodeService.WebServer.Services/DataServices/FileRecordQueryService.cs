using NodeService.Infrastructure.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;

namespace NodeService.WebServer.Services.DataServices;

public class FileRecordQueryService
{
    readonly ExceptionCounter _exceptionCounter;
    readonly ILogger<FileRecordQueryService> _logger;
    readonly ApplicationRepositoryFactory<FileRecordModel> _applicationRepoFactory;

    public FileRecordQueryService(
        ExceptionCounter exceptionCounter,
        ILogger<FileRecordQueryService> logger,
        ApplicationRepositoryFactory<FileRecordModel> appicationRepoFactory
    )
    {
        _exceptionCounter = exceptionCounter;
        _logger = logger;
        _applicationRepoFactory = appicationRepoFactory;
    }

    public async ValueTask<ListQueryResult<FileRecordModel>> QueryAsync(
        QueryFileRecordListParameters queryParameters,
        CancellationToken cancellationToken = default)
    {
        ListQueryResult<FileRecordModel> queryResult = default;
        await using var repo = await _applicationRepoFactory.CreateRepositoryAsync(cancellationToken);
        queryResult = await repo.PaginationQueryAsync(
            new FileRecordListSpecification(
                queryParameters.Category,
                queryParameters.State,
                DataFilterCollection<string>.Includes(queryParameters.NodeIdList),
                DataFilterCollection<string>.Includes(string.IsNullOrEmpty(queryParameters.Keywords)
                    ? []
                    : [queryParameters.Keywords]),
                queryParameters.SortDescriptions),
            new PaginationInfo(
                queryParameters.PageIndex,
                queryParameters.PageSize), cancellationToken);
        return queryResult;
    }

    public async ValueTask DeleteAsync(
        FileRecordModel fileRecord,
        CancellationToken cancellationToken = default)
    {
        await using var repo = await _applicationRepoFactory.CreateRepositoryAsync(cancellationToken);
        await repo.DeleteAsync(fileRecord, cancellationToken);
    }

    public async ValueTask AddOrUpdateAsync(
        FileRecordModel fileRecord,
        CancellationToken cancellationToken = default)
    {
        await using var repo = await _applicationRepoFactory.CreateRepositoryAsync(cancellationToken);
        var modelFromRepo = await repo.DbContext.Set<FileRecordModel>().FindAsync(new object?[] { fileRecord.Id, fileRecord.Name }, cancellationToken);
        if (modelFromRepo == null)
        {
            await repo.AddAsync(fileRecord, cancellationToken);
        }
        else
        {
            modelFromRepo.With(fileRecord);
            await repo.SaveChangesAsync(cancellationToken);
        }
    }
}