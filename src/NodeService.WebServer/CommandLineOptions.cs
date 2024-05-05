namespace NodeService.WebServer;

public class CommandLineOptions
{
    [Option("env", HelpText = "env")] public string env { get; set; }

    [Option("urls", HelpText = "urls")] public string urls { get; set; }
}