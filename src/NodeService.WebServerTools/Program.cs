using Microsoft.EntityFrameworkCore;
using NodeService.Infrastructure;
using NodeService.WebServer.Data;
using NodeService.WebServerTools.Services;

namespace NodeService.WebServerTools;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("Hello, World!");

        Environment.CurrentDirectory = AppContext.BaseDirectory;

        try
        {
            var builder = Host.CreateApplicationBuilder(args);

            // Add services to the container.

            Configure(builder);

            using var app = builder.Build();

            await app.RunAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    private static void Configure(HostApplicationBuilder builder)
    {
        ConfigureDbContext(builder);
        builder.Services.AddHostedService<AnalysisLogService>();
        builder.Services.AddSingleton<HttpClient>(sp =>
        {
            return new HttpClient
            {
                BaseAddress = new Uri("http://172.27.242.223:50060")
            };
        });
        builder.Services.AddSingleton<ApiService>();
        builder.Services.AddLogging(logger =>
        {
            logger.ClearProviders();
            logger.AddConsole();
        });
    }

    private static void ConfigureDbContext(HostApplicationBuilder builder)
    {
        builder.Services.AddPooledDbContextFactory<ApplicationDbContext>(options =>
            options.UseMySql(builder.Configuration.GetConnectionString("NodeServiceDbMySQL"),
                MySqlServerVersion.LatestSupportedServerVersion, mySqlOptionBuilder =>
                {
                    mySqlOptionBuilder.EnableRetryOnFailure();
                    mySqlOptionBuilder.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                    mySqlOptionBuilder.EnableStringComparisonTranslations();
                }));
        builder.Services.AddDbContext<ApplicationUserDbContext>(options =>
            options.UseMySql(builder.Configuration.GetConnectionString("NodeServiceUserDbMySQL"),
                MySqlServerVersion.LatestSupportedServerVersion, mySqlOptionBuilder =>
                {
                    mySqlOptionBuilder.EnableRetryOnFailure();
                    mySqlOptionBuilder.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                    mySqlOptionBuilder.EnableStringComparisonTranslations();
                }));
        builder.Services.AddDbContext<ApplicationProfileDbContext>(options =>
            options.UseMySql(builder.Configuration.GetConnectionString("MyProfileSQL"),
                MySqlServerVersion.LatestSupportedServerVersion, mySqlOptionBuilder =>
                {
                    mySqlOptionBuilder.EnableRetryOnFailure();
                    mySqlOptionBuilder.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                    mySqlOptionBuilder.EnableStringComparisonTranslations();
                }));
    }
}