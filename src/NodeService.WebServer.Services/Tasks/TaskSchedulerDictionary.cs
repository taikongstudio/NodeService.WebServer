namespace NodeService.WebServer.Services.Tasks;

public readonly record struct TaskSchedulerKey
{
    public TaskSchedulerKey(string key, TaskTriggerSource triggerSource)
    {
        Key = key;
        TriggerSource = triggerSource;
    }

    public string Key { get; init; }

    public TaskTriggerSource TriggerSource { get; init; }
}

public class TaskSchedulerDictionary : ConcurrentDictionary<TaskSchedulerKey, IAsyncDisposable>
{
}