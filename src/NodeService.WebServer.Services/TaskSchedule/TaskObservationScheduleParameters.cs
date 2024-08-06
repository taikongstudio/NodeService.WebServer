namespace NodeService.WebServer.Services.TaskSchedule;

public readonly record struct TaskObservationScheduleParameters
{
    public string ConfigurationId { get; init; }

    public TaskObservationScheduleParameters(
        string configurationId)
    {
        ConfigurationId = configurationId;
    }
}
