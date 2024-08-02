namespace NodeService.WebServer.Services.TaskSchedule;

public readonly record struct NodeHealthyCheckScheduleParameters
{
    public string ConfigurationId { get; init; }

    public NodeHealthyCheckScheduleParameters(
        string configurationId)
    {
        ConfigurationId = configurationId;
    }
}
