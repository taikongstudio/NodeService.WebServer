using System.Collections.Immutable;

namespace NodeService.WebServer.Services.Tasks;

public record struct FireTaskParameters
{
    public string TaskDefinitionId { get; init; }
    public string TaskActivationRecordId { get; init; }
    public TriggerSource TriggerSource { get; init; }
    public DateTimeOffset? NextFireTimeUtc { get; init; }
    public DateTimeOffset? PreviousFireTimeUtc { get; init; }
    public DateTimeOffset? ScheduledFireTimeUtc { get; init; }
    public DateTimeOffset FireTimeUtc { get; init; }

    public string? ParentTaskExecutionInstanceId { get; init; }

    public ImmutableArray<StringEntry> NodeList { get; init; }

    public ImmutableArray<StringEntry> EnvironmentVariables { get; init; }

    public TaskFlowTaskKey TaskFlowTaskKey { get; init; }

    public bool RetryTasks { get; init; }

    public static FireTaskParameters BuildRetryTaskParameters(
        string taskActiveRecordId,
        string taskDefinitionId,
        ImmutableArray<StringEntry> nodeList,
        ImmutableArray<StringEntry> envVars,
        TaskFlowTaskKey taskFlowTaskKey = default)
    {
        return new FireTaskParameters
        {
            FireTimeUtc = DateTime.UtcNow,
            TriggerSource = TriggerSource.Manual,
            TaskActivationRecordId = taskActiveRecordId,
            TaskDefinitionId = taskDefinitionId,
            ScheduledFireTimeUtc = DateTime.UtcNow,
            NodeList = nodeList,
            EnvironmentVariables = envVars,
            TaskFlowTaskKey = taskFlowTaskKey,
            RetryTasks = true,
        };
    }
}
