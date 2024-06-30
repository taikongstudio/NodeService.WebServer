namespace NodeService.WebServer.Services.Tasks;

public readonly record struct TaskSchedulerKey
{
    public TaskSchedulerKey(string key, TriggerSource triggerSource, string context)
    {
        Key = key;
        TriggerSource = triggerSource;
        Context = context;
    }

    public string Key { get; init; }

    public TriggerSource TriggerSource { get; init; }

    public string Context { get; init; }
}

public class TaskSchedulerDictionary : ConcurrentDictionary<TaskSchedulerKey, IAsyncDisposable>
{
}