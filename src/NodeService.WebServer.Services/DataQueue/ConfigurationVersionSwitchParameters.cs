namespace NodeService.WebServer.Services.DataQueue;

public record struct ConfigurationVersionSwitchParameters
{
    public ConfigurationVersionSwitchParameters(string configurationId, int targetVersion)
    {
        ConfigurationId = configurationId;
        TargetVersion = targetVersion;
    }

    public string ConfigurationId { get; set; }
    public int TargetVersion { get; set; }
}
