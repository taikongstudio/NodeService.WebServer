using System.Linq.Expressions;

namespace NodeService.WebServer.Data;

public static class IQueryableExtensions
{
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