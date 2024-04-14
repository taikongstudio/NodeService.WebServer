namespace NodeService.WebServer.Services.Tasks
{
    public readonly record struct JobSchedulerKey
    {
        public JobSchedulerKey(string key, JobTriggerSource triggerSource)
        {
            Key = key;
            TriggerSource = triggerSource;
        }

        public string Key { get; init; }

        public JobTriggerSource TriggerSource { get; init; }
    }

    public class TaskSchedulerDictionary : ConcurrentDictionary<JobSchedulerKey, IAsyncDisposable>
    {

    }
}
