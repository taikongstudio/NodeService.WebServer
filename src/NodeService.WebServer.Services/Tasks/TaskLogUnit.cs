using NodeService.Infrastructure.Logging;
using System.Collections.Immutable;

namespace NodeService.WebServer.Services.Tasks;

public record class TaskLogUnit
{
    public required string Id { get; init; }

    public required IEnumerable<LogEntry> LogEntries { get; init; } = [];

    public TaskExecutionStatus Status { get; init; }
}