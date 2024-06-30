namespace NodeService.WebServer.Services.Tasks;

public abstract class JobBase : IJob, IAsyncDisposable
{
    public JobBase(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
    }

    public IAsyncDisposable? AsyncDispoable { get; set; }


    public ILogger Logger { get; protected set; }

    public IServiceProvider ServiceProvider { get; private set; }


    public TriggerSource TriggerSource { get; set; }

    public required IDictionary<string, object?> Properties { get; set; }

    public async ValueTask DisposeAsync()
    {
        if (AsyncDispoable != null) await AsyncDispoable.DisposeAsync();
    }

    public abstract Task Execute(IJobExecutionContext context);
}