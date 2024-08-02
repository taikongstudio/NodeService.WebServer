using OneOf;

namespace NodeService.WebServer.Services.TaskSchedule;

public readonly record struct TaskScheduleServiceParameters
{
    public TaskScheduleServiceParameters(TaskDefinitionScheduleParameters parameters)
    {
        Parameters = parameters;
    }

    public TaskScheduleServiceParameters(TaskFlowScheduleParameters parameters)
    {
        Parameters = parameters;
    }

    public TaskScheduleServiceParameters(NodeHealthyCheckScheduleParameters parameters)
    {
        Parameters = parameters;
    }

    public OneOf<TaskDefinitionScheduleParameters, TaskFlowScheduleParameters, NodeHealthyCheckScheduleParameters> Parameters { get; init; }
}
