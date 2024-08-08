using System.Collections.Immutable;

namespace NodeService.WebServer.Services.Tasks;

public readonly struct FireTaskFlowParameters
{
    public FireTaskFlowParameters()
    {
    }

    public string TaskFlowTemplateId { get; init; }
    public string TaskFlowInstanceId { get; init; }
    public string? TaskFlowParentInstanceId { get; init; }
    public TriggerSource TriggerSource { get; init; }
    public DateTimeOffset? NextFireTimeUtc { get; init; }
    public DateTimeOffset? PreviousFireTimeUtc { get; init; }
    public DateTimeOffset? ScheduledFireTimeUtc { get; init; }
    public DateTimeOffset FireTimeUtc { get; init; }

    public ImmutableArray<StringEntry> EnvironmentVariables { get; init; } = [];
}
