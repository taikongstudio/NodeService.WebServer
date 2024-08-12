using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NodeService.Infrastructure;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.DataServices;
using NodeService.WebServerTools.Services;
using StackExchange.Redis;
using System.Diagnostics;

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
        builder.Services.AddHostedService<ClearConfigService>();
        //builder.Services.AddHostedService<UpdateOfflineNodeService>();
        //builder.Services.AddHostedService<TestConfigService>();
        builder.Services.AddSingleton<HttpClient>(sp =>
        {
            return new HttpClient
            {
                BaseAddress = new Uri("http://172.27.242.223:50060")
            };
        });
        builder.Services.AddSingleton(typeof(ApplicationRepositoryFactory<>));
        builder.Services.AddSingleton<NodeInfoQueryService>();
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<ApiService>();
        builder.Services.AddSingleton<ExceptionCounter>();
        builder.Services.AddSingleton<ObjectCache>();

        if (Debugger.IsAttached)
        {
            builder.Services.AddDistributedMemoryCache();
        }
        else
        {
            builder.Services.AddStackExchangeRedisCache(options =>
            {
                var endPointsString = builder.Configuration.GetValue<string>("RedisOptions:EndPoints");
                var password = builder.Configuration.GetValue<string>("RedisOptions:Password");
                var channel = builder.Configuration.GetValue<string>("RedisOptions:Channel");
                var endPoints = new EndPointCollection();
                foreach (var endPointString in endPointsString.Split(','))
                {
                    var endPoint = EndPointCollection.TryParse(endPointsString);
                    if (endPoint == null)
                    {
                        throw new InvalidOperationException("");
                    }
                    endPoints.Add(endPoint);
                }
                if (channel == "Debug")
                {
                    options.InstanceName = "NodeService_" + channel;
                }
                else
                {
                    options.InstanceName = "NodeService";
                }
                options.ConfigurationOptions = new ConfigurationOptions()
                {

                    EndPoints = endPoints,
                    Password = password,
                };
            });
        }

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
        builder.Services.AddPooledDbContextFactory<LimsDbContext>(options =>
        options.UseMySql(builder.Configuration.GetConnectionString("LIMSMySQL"),
            MySqlServerVersion.LatestSupportedServerVersion, mySqlOptionBuilder =>
            {
                mySqlOptionBuilder.EnableRetryOnFailure();
                mySqlOptionBuilder.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                mySqlOptionBuilder.EnableStringComparisonTranslations();
            }));

    }
}