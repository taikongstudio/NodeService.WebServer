namespace NodeService.WebServer.Services.TaskSchedule;

public readonly record struct TaskDefinitionScheduleParameters
{
    public string TaskDefinitionId { get; init; }

    public TriggerSource TriggerSource { get; init; }

    public TaskDefinitionScheduleParameters(
        TriggerSource triggerSource,
        string taskDefinitionId)
    {
        TriggerSource = triggerSource;
        TaskDefinitionId = taskDefinitionId;
    }
}
