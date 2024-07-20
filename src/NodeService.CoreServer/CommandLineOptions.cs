namespace NodeService.CoreServer;

public class CommandLineOptions
{
    [Option("env", HelpText = "env")] public string env { get; set; }

    [Option("urls", HelpText = "urls")] public string urls { get; set; }

    [Option("features", HelpText = "features")] public string features { get; set; }
}