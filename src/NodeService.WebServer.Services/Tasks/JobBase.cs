namespace NodeService.WebServer.Services.Tasks;

public abstract class JobBase : IJob, IAsyncDisposable
{
    public IAsyncDisposable? AsyncDispoable { get; set; }


    public required ILogger Logger { get; set; }

    public required IServiceProvider ServiceProvider { get; set; }


    public TaskTriggerSource TriggerSource { get; set; }

    public required IDictionary<string, object?> Properties { get; set; }

    public async ValueTask DisposeAsync()
    {
        if (AsyncDispoable != null) await AsyncDispoable.DisposeAsync();
    }

    public abstract Task Execute(IJobExecutionContext context);
}