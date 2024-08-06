namespace NodeService.WebServer.Services.Tasks;

public readonly record struct RetryTaskParameters
{
    public RetryTaskParameters(string taskExecutionInstanceId)
    {
        TaskExecutionInstanceId = taskExecutionInstanceId;
    }

    public string TaskExecutionInstanceId { get; init; }
}
