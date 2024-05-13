using Ardalis.Specification;
using Microsoft.EntityFrameworkCore;
using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Models;
using System.Linq.Expressions;

namespace NodeService.WebServer.Extensions;

public static class QueryableExtensions
{
    public static async Task<PaginationResponse<T>> QueryPageItemsAsync<T>(
        this IQueryable<T> queryable,
        PaginationQueryParameters queryParameters,
        CancellationToken cancellationToken = default)
    {
        PaginationResponse<T> apiResponse = new PaginationResponse<T>();

        T[] items = [];
        var totalCount = 0;

        if (queryParameters.PageIndex <= 0 && queryParameters.PageSize <= 0)
        {
            items = await queryable.ToArrayAsync(cancellationToken);
            totalCount = items.Length;
        }
        else
        {
            totalCount = await queryable.CountAsync(cancellationToken);
            var pageIndex = queryParameters.PageIndex - 1;
            var pageSize = queryParameters.PageSize;
            var startIndex = pageIndex * pageSize;
            var skipCount = totalCount > startIndex ? startIndex : 0;
            items = await queryable
                            .Skip(skipCount)
                            .Take(pageSize)
                            .ToArrayAsync(cancellationToken);
            var pageCount = totalCount > 0 ? Math.DivRem(totalCount, pageSize, out var _) + 1 : 0;
            if (queryParameters.PageIndex > pageCount) queryParameters.PageIndex = pageCount;
            if (totalCount < pageSize && pageCount == 1)
            {
                queryParameters.PageIndex = 1;
                queryParameters.PageSize = pageSize;
            }
        }

        apiResponse.SetTotalCount(totalCount);
        apiResponse.SetPageIndex(queryParameters.PageIndex);
        apiResponse.SetPageSize(queryParameters.PageSize);
        apiResponse.SetResult(items);
        return apiResponse;
    }

    public static IQueryable<T> OrderBy<T>(
        this IQueryable<T> queryable,
        IEnumerable<SortDescription> sortDescriptions,
        Func<string, string>? pathSelector = null)
    {
        IOrderedQueryable<T>? orderedQueryable = null;
        foreach (var sortDescription in sortDescriptions)
        {
            var path = pathSelector?.Invoke(sortDescription.Name) ?? sortDescription.Name;


            if (string.IsNullOrEmpty(sortDescription.Direction) 
                ||
                sortDescription.Direction.StartsWith(
                "asc",
                StringComparison.OrdinalIgnoreCase))
            {
                if (orderedQueryable == null)
                    orderedQueryable = queryable.OrderByColumnUsing(path, sortDescription.Direction);
                else
                    orderedQueryable = orderedQueryable.ThenByColumnUsing(path, sortDescription.Direction);
            }
            else if (sortDescription.Direction.StartsWith("desc", StringComparison.OrdinalIgnoreCase))
            {
                if (orderedQueryable == null)
                    orderedQueryable = queryable.OrderByColumnUsing(path, sortDescription.Direction);
                else
                    orderedQueryable = orderedQueryable.ThenByColumnUsing(path, sortDescription.Direction);
            }
        }

        if (orderedQueryable != null) queryable = orderedQueryable;
        return queryable;
    }

    public static ISpecificationBuilder<T> SortBy<T>(
    this ISpecificationBuilder<T> builder,
    IEnumerable<SortDescription> sortDescriptions,
    Func<string, string>? pathSelector = null)
    {
        IOrderedSpecificationBuilder<T>? orderedQueryable = null;
        foreach (var sortDescription in sortDescriptions)
        {
            var path = pathSelector?.Invoke(sortDescription.Name) ?? sortDescription.Name;
            var expr = CreateKeySelectorExpression<T>(path);
            if (string.IsNullOrEmpty(sortDescription.Direction)
                ||
                sortDescription.Direction.StartsWith(
                "asc",
                StringComparison.OrdinalIgnoreCase))
            {
                if (orderedQueryable == null)
                {
                    orderedQueryable = builder.OrderBy(expr);
                }
                else
                {
                    orderedQueryable = orderedQueryable.ThenBy(expr);
                }

            }
            else if (sortDescription.Direction.StartsWith("desc", StringComparison.OrdinalIgnoreCase))
            {
                if (orderedQueryable == null)
                {
                    orderedQueryable = builder.OrderByDescending(expr);
                }
                else
                {
                    orderedQueryable = orderedQueryable.ThenByDescending(expr);
                }
            }
        }

        return builder;
    }

    private static Expression<Func<T, object?>> CreateKeySelectorExpression<T>(
        string columnPath)
    {
        var parameter = Expression.Parameter(typeof(T), "item");
        var member = columnPath.Split('.')
            .Aggregate((Expression)parameter, Expression.PropertyOrField);
        var keySelector = Expression.Lambda(Expression.Convert(member, typeof(object)), parameter);
        return (Expression<Func<T, object?>>)keySelector;
    }

    private static IOrderedQueryable<T>? CreateOrderQuery<T>(
    IQueryable<T> source,
    string columnPath,
    string? sortDirection,
    string ascMethodName,
    string descMethodName)
    {
        var parameter = Expression.Parameter(typeof(T), "item");
        var member = columnPath.Split('.')
            .Aggregate((Expression)parameter, Expression.PropertyOrField);
        var keySelector = Expression.Lambda(member, parameter);
        var methodName = nameof(Queryable.OrderBy);
        if (string.IsNullOrEmpty(sortDirection)
            ||
            sortDirection.StartsWith("asc", StringComparison.OrdinalIgnoreCase))
            methodName = ascMethodName;
        else if (sortDirection.StartsWith("desc", StringComparison.OrdinalIgnoreCase))
            methodName = descMethodName;
        var methodCall = Expression.Call(typeof(Queryable), methodName, [parameter.Type, member.Type],
            source.Expression, Expression.Quote(keySelector));

        return source.Provider.CreateQuery(methodCall) as IOrderedQueryable<T>;
    }


    public static IOrderedQueryable<T>? OrderByColumnUsing<T>(
        this IQueryable<T> source,
        string columnPath,
        string? sortDirection)
    {
        string ascMethodName = nameof(Queryable.OrderBy);
        string descMethodName = nameof(Queryable.OrderByDescending);
        return CreateOrderQuery(source, columnPath, sortDirection, ascMethodName, descMethodName);
    }

    public static IOrderedQueryable<T>? ThenByColumnUsing<T>(
        this IQueryable<T> source,
        string columnPath,
        string sortDirection)
    {
        string ascMethodName = nameof(Queryable.ThenBy);
        string descMethodName = nameof(Queryable.ThenByDescending);
        return CreateOrderQuery(source, columnPath, sortDirection, ascMethodName, descMethodName);
    }
}