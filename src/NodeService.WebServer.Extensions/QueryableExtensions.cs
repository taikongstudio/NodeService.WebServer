using Microsoft.EntityFrameworkCore;
using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Models;
using System.Linq.Expressions;

namespace NodeService.WebServer.Extensions;

public static class QueryableExtensions
{
    public static async Task<PaginationResponse<T>> QueryPageItemsAsync<T>(this IQueryable<T> queryable, PaginationQueryParameters queryParameters, CancellationToken cancellationToken = default)
    {
        PaginationResponse<T> apiResponse = new PaginationResponse<T>();

        T[] items = [];
        var totalCount = 0;

        if (queryParameters.PageIndex <= 0 && queryParameters.PageSize <= 0)
        {
            items = await queryable.ToArrayAsync();
            totalCount = items.Length;
        }
        else
        {
            totalCount = await queryable.CountAsync();
            var pageIndex = queryParameters.PageIndex - 1;
            var pageSize = queryParameters.PageSize;
            var startIndex = pageIndex * pageSize;
            var skipCount = totalCount > startIndex ? startIndex : 0;
            items = await queryable
                            .Skip(skipCount)
                            .Take(pageSize)
                            .ToArrayAsync();
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
       this IQueryable<T> queryable, IEnumerable<SortDescription> sortDescriptions, Func<string, string>? mappingPathFunc = null)
    {
        var isFirstOrder = true;
        IOrderedQueryable<T>? orderedQueryable = null;
        foreach (var sortDescription in sortDescriptions)
        {
            var path = mappingPathFunc?.Invoke(sortDescription.Name) ?? sortDescription.Name;


            if (sortDescription.Direction == "ascend" || string.IsNullOrEmpty(sortDescription.Direction))
            {
                if (isFirstOrder)
                    orderedQueryable = queryable.OrderByColumnUsing(path, sortDescription.Direction);
                else
                    orderedQueryable = orderedQueryable.ThenByColumnUsing(path, sortDescription.Direction);
            }
            else if (sortDescription.Direction == "descend")
            {
                if (isFirstOrder)
                    orderedQueryable = queryable.OrderByColumnUsing(path, sortDescription.Direction);
                else
                    orderedQueryable = orderedQueryable.ThenByColumnUsing(path, sortDescription.Direction);
            }

            isFirstOrder = false;
        }

        if (orderedQueryable != null) queryable = orderedQueryable;
        return queryable;
    }


    public static IOrderedQueryable<T> OrderByColumnUsing<T>(this IQueryable<T> source, string columnPath,
        string sortDirection)
    {
        var parameter = Expression.Parameter(typeof(T), "item");
        var member = columnPath.Split('.')
            .Aggregate((Expression)parameter, Expression.PropertyOrField);
        var keySelector = Expression.Lambda(member, parameter);
        var methodName = nameof(Queryable.OrderBy);
        if (sortDirection == "ascend" || string.IsNullOrEmpty(methodName))
            methodName = nameof(Queryable.OrderBy);
        else if (sortDirection == "descend") methodName = nameof(Queryable.OrderByDescending);
        var methodCall = Expression.Call(typeof(Queryable), methodName, [parameter.Type, member.Type],
            source.Expression, Expression.Quote(keySelector));

        return (IOrderedQueryable<T>)source.Provider.CreateQuery(methodCall);
    }

    public static IOrderedQueryable<T> ThenByColumnUsing<T>(this IQueryable<T> source, string columnPath,
        string sortDirection)
    {
        var parameter = Expression.Parameter(typeof(T), "item");
        var member = columnPath.Split('.')
            .Aggregate((Expression)parameter, Expression.PropertyOrField);
        var keySelector = Expression.Lambda(member, parameter);
        var methodName = nameof(Queryable.ThenBy);
        if (sortDirection == "asc" || string.IsNullOrEmpty(methodName))
            methodName = nameof(Queryable.ThenBy);
        else if (sortDirection == "desc") methodName = nameof(Queryable.ThenByDescending);
        var methodCall = Expression.Call(typeof(Queryable), methodName, [parameter.Type, member.Type],
            source.Expression, Expression.Quote(keySelector));

        return (IOrderedQueryable<T>)source.Provider.CreateQuery(methodCall);
    }
}