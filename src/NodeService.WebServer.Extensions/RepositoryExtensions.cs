using Ardalis.Specification;
using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.DataModels;
using NodeService.WebServer.Data;

namespace NodeService.WebServer.Extensions;

public static class RepositoryExtensions
{
    public static async Task<ListQueryResult<T>> PaginationQueryAsync<T>(
        this IRepository<T> repository,
        ISpecification<T> specification,
        int pageSize = 0,
        int pageIndex = 0,
        CancellationToken cancellationToken = default)
        where T : class, IAggregateRoot
    {
        ArgumentNullException.ThrowIfNull(nameof(repository));
        ArgumentNullException.ThrowIfNull(nameof(specification));

        List<T> items = [];
        if (pageIndex <= 0) pageIndex = 1;

        var totalCount = await repository.CountAsync(specification, cancellationToken);

        if (totalCount > 0)
        {
            if (pageSize <= 0) pageSize = totalCount;

            var pageCount = totalCount > 0 ? Math.DivRem(totalCount, pageSize, out var _) + 1 : 0;


            if (pageIndex > pageCount) pageIndex = pageCount;

            var startIndex = (pageIndex - 1) * pageSize;

            ArgumentOutOfRangeException.ThrowIfGreaterThan(startIndex, totalCount, nameof(startIndex));

            startIndex = totalCount > startIndex ? startIndex : 0;

            if (startIndex >= 0 && pageSize > 0) specification.Query.Skip(startIndex).Take(pageSize);
        }

        items = await repository.ListAsync(
            specification,
            cancellationToken);

        return new ListQueryResult<T>(
            totalCount,
            pageSize,
            pageIndex,
            items);
    }
}