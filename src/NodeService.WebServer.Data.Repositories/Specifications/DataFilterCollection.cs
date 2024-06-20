namespace NodeService.WebServer.Data.Repositories.Specifications;

public enum DataFilterTypes
{
    None,
    Include,
    Exclude,
}

public readonly struct DataFilterCollection<T>
{
    public static readonly DataFilterCollection<T> Empty = new();

    public DataFilterCollection(DataFilterTypes filterType, IEnumerable<T> items)
    {
        FilterType = filterType;
        Items = items ?? throw new ArgumentNullException(nameof(items));
    }

    public DataFilterTypes FilterType { get; init; }

    public IEnumerable<T> Items { get; init; }

    public bool HasValue => Items != null && Items.Any();

    public static DataFilterCollection<T> Includes(IEnumerable<T> items)
    {
        return new DataFilterCollection<T>(DataFilterTypes.Include, items);
    }

    public static DataFilterCollection<T> Includes(params T[] items)
    {
        return new DataFilterCollection<T>(DataFilterTypes.Include, items);
    }

    public static DataFilterCollection<T> Excludes(IEnumerable<T> items)
    {
        return new DataFilterCollection<T>(DataFilterTypes.Exclude, items);
    }

    public static DataFilterCollection<T> Excludes(params T[] items)
    {
        return new DataFilterCollection<T>(DataFilterTypes.Exclude, items);
    }
}