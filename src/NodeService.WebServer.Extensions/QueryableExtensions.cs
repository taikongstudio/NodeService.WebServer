using System.Linq.Expressions;
using Ardalis.Specification;
using Microsoft.EntityFrameworkCore;
using NodeService.Infrastructure.Models;

namespace NodeService.WebServer.Extensions;

public static class QueryableExtensions
{
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
                    orderedQueryable = builder.OrderBy(expr);
                else
                    orderedQueryable = orderedQueryable.ThenBy(expr);
            }
            else if (sortDescription.Direction.StartsWith("desc", StringComparison.OrdinalIgnoreCase))
            {
                if (orderedQueryable == null)
                    orderedQueryable = builder.OrderByDescending(expr);
                else
                    orderedQueryable = orderedQueryable.ThenByDescending(expr);
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
        var ascMethodName = nameof(Queryable.OrderBy);
        var descMethodName = nameof(Queryable.OrderByDescending);
        return CreateOrderQuery(source, columnPath, sortDirection, ascMethodName, descMethodName);
    }

    public static IOrderedQueryable<T>? ThenByColumnUsing<T>(
        this IQueryable<T> source,
        string columnPath,
        string sortDirection)
    {
        var ascMethodName = nameof(Queryable.ThenBy);
        var descMethodName = nameof(Queryable.ThenByDescending);
        return CreateOrderQuery(source, columnPath, sortDirection, ascMethodName, descMethodName);
    }
}