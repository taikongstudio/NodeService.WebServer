using NodeService.Infrastructure.Logging;
using System.Collections.Immutable;

namespace NodeService.WebServer.Services.Tasks;

public class TaskLogUnit
{
    public required string Id { get; set; }

    public required ImmutableArray<LogEntry> LogEntries { get; set; } = [];

    public TaskExecutionStatus Status { get; set; }
}