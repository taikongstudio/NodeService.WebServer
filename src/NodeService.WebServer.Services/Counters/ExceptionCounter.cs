using System.Collections.Concurrent;

namespace NodeService.WebServer.Services.Counters;

public class ExceptionCounter
{
    private readonly ConcurrentDictionary<string, int> _dict;

    public ExceptionCounter()
    {
        _dict = new ConcurrentDictionary<string, int>();
    }

    public void AddOrUpdate(
        Exception exception,
        string? nodeId = null,
        [CallerFilePath] string? filePath = null,
        [CallerLineNumber] int lineNumber = 0,
        [CallerMemberName] string? caller = null)
    {
        var msg = $"{nodeId}:{filePath}:{lineNumber} {caller} {exception}";
        _dict.AddOrUpdate(msg, 1, UpdateValueImpl);
    }

    private int UpdateValueImpl(string key, int value)
    {
        return value + 1;
    }

    public IEnumerable<(string Exception, int Count)> GetStatistics()
    {
        foreach (var kv in _dict) yield return (kv.Key, kv.Value);
    }
}