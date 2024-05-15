namespace NodeService.WebServer.Data.Repositories.Specifications;

public enum DataFilterTypes
{
    None,
    Include,
    Exclude
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

    public static DataFilterCollection<string> Includes(IEnumerable<string> items)
    {
        return new DataFilterCollection<string>(DataFilterTypes.Include, items);
    }

    public static DataFilterCollection<string> Includes(params string[] items)
    {
        return new DataFilterCollection<string>(DataFilterTypes.Include, items);
    }

    public static DataFilterCollection<string> Excludes(IEnumerable<string> items)
    {
        return new DataFilterCollection<string>(DataFilterTypes.Exclude, items);
    }

    public static DataFilterCollection<string> Excludes(params string[] items)
    {
        return new DataFilterCollection<string>(DataFilterTypes.Exclude, items);
    }
}