namespace NodeService.WebServer.Services.Tasks
{
    public abstract class JobBase : IJob, IAsyncDisposable
    {

        public IAsyncDisposable AsyncDispoable { get; set; }


        public ILogger Logger { get; set; }

        public IServiceProvider ServiceProvider { get; set; }


        public JobTriggerSource TriggerSource { get; set; }

        public IDictionary<string, object?> Properties { get; set; }

        public async ValueTask DisposeAsync()
        {
            if (AsyncDispoable != null)
            {
                await AsyncDispoable.DisposeAsync();
            }
        }

        public abstract Task Execute(IJobExecutionContext context);
    }
}