using NodeService.WebServer.Services.NodeSessions;
using System.Collections.Immutable;

namespace NodeService.WebServer.Services.Tasks;


internal class TaskExecutionInstanceProcessContext
{
    public TaskExecutionInstanceProcessContext(
        TaskExecutionInstanceModel taskExecutionInstance,
        ImmutableArray<TaskExecutionReport> reports)
    {
        Instance = taskExecutionInstance;
        Reports = reports;
    }

    public TaskExecutionInstanceModel Instance { get; set; }

    public TaskActivationRecordModel TaskActivationRecord { get; set; }

    public ImmutableArray<TaskExecutionReport> Reports { get; set; } = [];

    public bool StatusChanged { get; set; }

    public bool MessageChanged { get; set; }
}