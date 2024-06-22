using System.Collections.Concurrent;

namespace NodeService.WebServer.Services.Counters;

public record class ExecptionKey
{
    public string? Source { get; init; }

    public string Exception { get; init; }
}

public  class ExecptionEntry
{
    public ExecptionEntry(string? source, string exception, int count)
    {
        Source = source;
        Exception = exception;
        Count = count;
    }

    public string? Source { get; set; }

    public string Exception { get; set; }

    public int Count { get; set; }
}


public class ExceptionCounter
{

    private readonly ConcurrentDictionary<ExecptionKey, int> _dict;

    public ExceptionCounter()
    {
        _dict = new ConcurrentDictionary<ExecptionKey, int>();
    }

    public void AddOrUpdate(
        Exception exception,
        string? source = null,
        [CallerFilePath] string? filePath = null,
        [CallerLineNumber] int lineNumber = 0,
        [CallerMemberName] string? caller = null)
    {
        var msg = $"{source}:{filePath}:{lineNumber} {caller} {exception}";
        _dict.AddOrUpdate(new ExecptionKey()
        {
            Exception = exception.ToString(),
            Source = source,
        }, 1, UpdateValueImpl);
    }

    private int UpdateValueImpl(ExecptionKey key, int value)
    {
        return value + 1;
    }

    public IEnumerable<ExecptionEntry> GetStatistics()
    {
        foreach (var kv in _dict) yield return new(kv.Key.Source, kv.Key.Exception, kv.Value);
    }
}