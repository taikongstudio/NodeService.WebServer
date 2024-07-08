﻿using Microsoft.EntityFrameworkCore.Metadata;
using System.Collections.Concurrent;

namespace NodeService.WebServer.Data.Repositories;

public static class RepositoryExtensions
{
    private static readonly ConcurrentDictionary<IEntityType, bool> _optimizeQueryEntityDict = new();

    public static Task<ListQueryResult<T>> PaginationQueryAsync<T>(
        this IRepository<T> repository,
        ISpecification<T> specification,
        PaginationInfo paginationInfo,
        CancellationToken cancellationToken = default)
        where T : RecordBase
    {
        return repository.PaginationQueryAsync(specification, paginationInfo.PageSize, paginationInfo.PageIndex,
            cancellationToken);
    }

    public static async Task<ListQueryResult<T>> PaginationQueryAsync<T>(
        this IRepository<T> repository,
        ISpecification<T> specification,
        int pageSize = 0,
        int pageIndex = 0,
        CancellationToken cancellationToken = default)
        where T : RecordBase
    {
        ArgumentNullException.ThrowIfNull(nameof(repository));
        ArgumentNullException.ThrowIfNull(nameof(specification));


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

        List<T>? items = null;
        var optimizeQuery = false;
        var entityType = repository.DbContext.Set<T>().EntityType;
        if (!_optimizeQueryEntityDict.TryGetValue(entityType, out optimizeQuery))
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.GetColumnType() == "json")
                {
                    optimizeQuery = true;
                    _optimizeQueryEntityDict.TryAdd(entityType, true);
                    break;
                }
            }
        }

        if (optimizeQuery)
        {

            if (typeof(T).IsAssignableTo(typeof(JsonBasedDataModel)))
            {
                if (specification is SelectSpecification<T, string> selectIdSpecification)
                {
                    selectIdSpecification.Query.Select(x => x.Id);
                    var idList = await repository.ListAsync(selectIdSpecification, cancellationToken);
                    if (idList == null)
                    {
                        items = [];
                    }
                    items = await repository.ListAsync(
                        new ListSpecification<T>(DataFilterCollection<string>.Includes(idList)),
                        cancellationToken);
                }
            }
        }

        if (items == null)
        {
            items = await repository.ListAsync(
                             specification,
                             cancellationToken);
        }

        return new ListQueryResult<T>(
            totalCount,
            pageIndex,
            pageSize,
            items);
    }
}