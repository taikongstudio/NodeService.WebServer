namespace NodeService.WebServer.Services.Tasks;

public record struct FireTaskParameters
{
    public string TaskDefinitionId { get; init; }
    public string FireInstanceId { get; init; }
    public TriggerSource TriggerSource { get; init; }
    public DateTimeOffset? NextFireTimeUtc { get; init; }
    public DateTimeOffset? PreviousFireTimeUtc { get; init; }
    public DateTimeOffset? ScheduledFireTimeUtc { get; init; }
    public DateTimeOffset FireTimeUtc { get; init; }

    public string? ParentTaskExecutionInstanceId { get; init; }

    public List<StringEntry> NodeList { get; init; }

    public List<StringEntry> EnvironmentVariables { get; init; }

    public TaskFlowTaskKey TaskFlowTaskKey { get; init; }

    public bool RetryTasks { get; set; }

    public static FireTaskParameters BuildRetryTaskParameters(
        string taskActiveRecordId,
        string taskDefinitionId,
        string fireInstanceId,
        List<StringEntry> nodeList,
        List<StringEntry> envVars,
        TaskFlowTaskKey taskFlowTaskKey = default)
    {
        return new FireTaskParameters
        {
            FireTimeUtc = DateTime.UtcNow,
            TriggerSource = TriggerSource.Manual,
            FireInstanceId = fireInstanceId,
            TaskDefinitionId = taskDefinitionId,
            ScheduledFireTimeUtc = DateTime.UtcNow,
            NodeList = nodeList,
            EnvironmentVariables = envVars,
            TaskFlowTaskKey = taskFlowTaskKey,
            RetryTasks = true,
        };
    }
}
