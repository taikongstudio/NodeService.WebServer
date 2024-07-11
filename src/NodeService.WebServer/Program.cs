using NodeService.WebServer.Servers;
using System.Text;

public class Program
{
    public static async Task Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        await Parser
            .Default
            .ParseArguments<CommandLineOptions>(args)
            .WithParsedAsync(options =>
            {
                if (string.IsNullOrEmpty(options.env)) options.env = Environments.Development;
                Console.WriteLine(JsonSerializer.Serialize(options));
                return RunWithOptions(options, args);
            });
    }

    private static async Task RunWithOptions(CommandLineOptions options, string[] args)
    {
        Environment.CurrentDirectory = AppContext.BaseDirectory;

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", options.env);

            if (string.IsNullOrEmpty(options.features))
            {
                var fullFeatureServer = new PrimaryServer(options, args);
                await fullFeatureServer.RunAsync(default);
            }
            else if (string.Equals(options.features, "Secondary", StringComparison.OrdinalIgnoreCase))
            {
                var fileUploadServer = new SecondaryServer(options, args);
                await fileUploadServer.RunAsync(default);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var crashFileName = $"./crash.{DateTime.Now:yyyy-MM-dd_HH_mm_ss}.log";
        if (e.IsTerminating) crashFileName = $"./crash.terminate.{DateTime.Now:yyyy-MM-dd_HH_mm_ss}.log";
        try
        {
            using var crashWriter = File.CreateText(crashFileName);
            crashWriter.WriteLine(e.ExceptionObject.ToString());
            crashWriter.Flush();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
        }
    }


}