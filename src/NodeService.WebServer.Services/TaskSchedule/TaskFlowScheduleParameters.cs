namespace NodeService.WebServer.Services.TaskSchedule;

public readonly record struct TaskFlowScheduleParameters
{
    public TaskFlowScheduleParameters(
        TriggerSource triggerSource,
        string taskFlowTemplateId)
    {
        TriggerSource = triggerSource;
        TaskFlowTemplateId = taskFlowTemplateId;
    }

    public string TaskFlowTemplateId { get; init; }

    public TriggerSource TriggerSource { get; init; }
}
