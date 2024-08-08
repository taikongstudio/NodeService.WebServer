namespace NodeService.WebServer.Services.DataServices;

public record struct ConfigurationVersionDeleteParameters
{
    public ConfigurationVersionDeleteParameters(ConfigurationVersionRecordModel value)
    {
        Value = value;
    }

    public ConfigurationVersionRecordModel Value { get; private set; }
}
