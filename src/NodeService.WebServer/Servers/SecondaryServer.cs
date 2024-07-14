using AntDesign.ProLayout;
using Microsoft.AspNetCore.RateLimiting;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.NodeFileSystem;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.DataQueue;
using NodeService.WebServer.Services.NodeFileSystem;
using NodeService.WebServer.Services.NodeSessions;
using NodeService.WebServer.Services.VirtualFileSystem;
using OpenTelemetry.Metrics;
using System.Text;
using System.Threading.RateLimiting;

namespace NodeService.WebServer.Servers
{
    public class SecondaryServer : WebServerBase
    {
        readonly CommandLineOptions _options;
        readonly string[] _args;

        public SecondaryServer(CommandLineOptions options, string[] args)
        {
            _options = options;
            _args = args;
        }

        async ValueTask ConfigureAsync(WebApplication app)
        {
            // Add OpenAPI 3.0 document serving middleware
            // Available at: http://localhost:<port>/swagger/v1/swagger.json
            app.UseOpenApi();
            app.UseRequestDecompression();
            // Add web UIs to interact with the document
            // Available at: http://localhost:<port>/swagger
            app.UseSwaggerUi(uiSettings => { });
            if (app.Environment.IsDevelopment())
            {
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

            //app.UseHttpsRedirection();
            app.UseHsts();

            app.UseStaticFiles();

            app.UseRouting();

            app.UseCors("AllowAll");

            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
            app.MapPrometheusScrapingEndpoint();
            app.MapRazorPages();
            app.UseRateLimiter();

            var factory = app.Services.GetService<IDbContextFactory<ApplicationDbContext>>();
            await using var dbContext = await factory.CreateDbContextAsync();
            dbContext.Database.EnsureCreated();

            using var scope = app.Services.CreateAsyncScope();
            using var applicationUserDbContext = scope.ServiceProvider.GetService<ApplicationUserDbContext>();
            applicationUserDbContext.Database.EnsureCreated();
        }

        void Configure(WebApplicationBuilder builder)
        {
            builder.Services.Configure<WebServerOptions>(builder.Configuration.GetSection(nameof(WebServerOptions)));
            builder.Services.Configure<FtpOptions>(builder.Configuration.GetSection(nameof(FtpOptions)));
            builder.Services.Configure<ProSettings>(builder.Configuration.GetSection(nameof(ProSettings)));
            builder.Services.Configure<FormOptions>(options => { options.MultipartBodyLengthLimit = 1024 * 1024 * 1024 * 4L; });

            builder.Services.AddRequestDecompression(options =>
            {

            });
            builder.Services.AddControllersWithViews();
            builder.Services.AddControllers();
            builder.Services.AddRazorPages();

            builder.WebHost.ConfigureKestrel(options => { options.Limits.MaxRequestLineSize = 8192 * 10; });

            builder.Services.AddOpenApiDocument();
            builder.Services.AddHttpClient();
            builder.Services.AddMemoryCache(options => { options.TrackStatistics = true; });
            builder.Services.AddDistributedMemoryCache(options => { });
            builder.Services.AddDatabaseDeveloperPageExceptionFilter();
            builder.Services.AddOpenTelemetry()
                .WithMetrics(builder =>
                {
                    builder.AddPrometheusExporter();

                    builder.AddMeter("Microsoft.AspNetCore.Hosting",
                        "Microsoft.AspNetCore.Server.Kestrel");
                    builder.AddView("http.server.request.duration",
                        new ExplicitBucketHistogramConfiguration
                        {
                            Boundaries =
                            [
                                0, 0.005, 0.01, 0.025, 0.05,
                            0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10
                            ]
                        });
                });

            ConfigureDbContext(builder);

            builder.Services.AddLogging(logger =>
            {
                logger.AddFilter("Microsoft.AspNetCore.Components.RenderTree.*", LogLevel.None);
                logger.ClearProviders();
                logger.AddConsole();
                logger.AddNLog();
            });

            ConfigureSingleton(builder);

            ConfigureScoped(builder);

            ConfigureHostedServices(builder);

            ConfifureCor(builder);

            ConfigureRateLimiter(builder);
        }

        void ConfifureCor(WebApplicationBuilder builder)
        {
            builder.Services.AddCors(o => o.AddPolicy("AllowAll", corPolicyBuilder =>
            {
                corPolicyBuilder.AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding")
                    .WithHeaders("Access-Control-Allow-Headers: *", "Access-Control-Allow-Origin: *");
            }));
        }

        void ConfigureRateLimiter(WebApplicationBuilder builder)
        {
            builder.Services.AddRateLimiter(_ => _
                .AddSlidingWindowLimiter("UploadFile", options =>
                {
                    options.PermitLimit = 1000;
                    options.Window = TimeSpan.FromSeconds(10);
                    options.SegmentsPerWindow = 10;
                    options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    options.QueueLimit = 10000;
                }));
        }

        void ConfigureHostedServices(WebApplicationBuilder builder)
        {
            builder.Services.AddHostedService<PackageQueryQueueService>();
            builder.Services.AddHostedService<ClientUpdateQueryQueueService>();
        }

        void ConfigureScoped(WebApplicationBuilder builder)
        {
            builder.Services.AddScoped(sp => new HttpClient
            {
                BaseAddress = new Uri(builder.Configuration.GetValue<string>("Kestrel:Endpoints:MyHttpEndpoint:Url"))
            });

            builder.Services.AddScoped(serviceProvider =>
            {
                var optionSnapshot = serviceProvider.GetService<IOptionsSnapshot<FtpOptions>>();
                var ftpServerConfig = optionSnapshot.Value;
                return new AsyncFtpClient(ftpServerConfig.Host,
                    ftpServerConfig.Username,
                    ftpServerConfig.Password,
                    ftpServerConfig.Port);
            });

            builder.Services.AddScoped<IVirtualFileSystem>(serviceProvider =>
            {
                var optionSnapshot = serviceProvider.GetService<IOptionsSnapshot<WebServerOptions>>();
                switch (optionSnapshot.Value.VirtualFileSystem)
                {
                    case "ftp":
                        var ftpClient = serviceProvider.GetService<AsyncFtpClient>();
                        return new FtpVirtualFileSystem(ftpClient);
                        break;
                    default:
                        return null;
                        break;
                }
            });

            builder.Services.AddScoped<ApiService>(serviceProvider =>
            {
                var httpClient = serviceProvider.GetService<HttpClient>();
                return new ApiService(httpClient);
            });
        }

        void ConfigureSingleton(WebApplicationBuilder builder)
        {
            builder.Services.AddSingleton<CommandLineOptions>(_options);
            builder.Services.AddSingleton<ExceptionCounter>();
            builder.Services.AddSingleton<WebServerCounter>();
            builder.Services.AddSingleton<NodeFileSyncQueueDictionary>();
            builder.Services.AddSingleton<SyncRecordQueryService>();
            builder.Services.AddSingleton(typeof(ApplicationRepositoryFactory<>));
            builder.Services.AddSingleton(new BatchQueue<AsyncOperation<PackageDownloadParameters, PackageDownloadResult>>(TimeSpan.FromSeconds(5), 1024));

            builder.Services.AddSingleton(new BatchQueue<AsyncOperation<ClientUpdateBatchQueryParameters, ClientUpdateConfigModel>>(TimeSpan.FromSeconds(1),
                    64));

        }

        void ConfigureDbContext(WebApplicationBuilder builder)
        {
            var debugProductionMode =
                builder.Configuration.GetValue<bool>(
                    $"{nameof(WebServerOptions)}:{nameof(WebServerOptions.DebugProductionMode)}");
            if ((builder.Environment.IsDevelopment() || Debugger.IsAttached) && !debugProductionMode)
            {
                builder.Services.AddPooledDbContextFactory<ApplicationDbContext>(options =>
                    options.UseSqlServer(
                        builder.Configuration.GetConnectionString("NodeServiceDbMySQL_debug"), optionsBuilder =>
                        {
                            optionsBuilder.EnableRetryOnFailure();
                            optionsBuilder.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                        }), 2048);
                builder.Services.AddDbContext<ApplicationUserDbContext>(options =>
                    options.UseSqlServer(
                        builder.Configuration.GetConnectionString("NodeServiceUserDbMySQL_debug"), optionsBuilder =>
                        {
                            optionsBuilder.EnableRetryOnFailure();
                            optionsBuilder.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                        }));
            }
            else
            {
                builder.Services.AddPooledDbContextFactory<ApplicationDbContext>(options =>
                    options.UseMySql(builder.Configuration.GetConnectionString("NodeServiceDbMySQL"),
                        MySqlServerVersion.LatestSupportedServerVersion, mySqlOptionBuilder =>
                        {
                            mySqlOptionBuilder.EnableRetryOnFailure();
                            mySqlOptionBuilder.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                            mySqlOptionBuilder.EnableStringComparisonTranslations();
                        }), 2048);
                builder.Services.AddDbContext<ApplicationUserDbContext>(options =>
                    options.UseMySql(builder.Configuration.GetConnectionString("NodeServiceUserDbMySQL"),
                        MySqlServerVersion.LatestSupportedServerVersion, mySqlOptionBuilder =>
                        {
                            mySqlOptionBuilder.EnableRetryOnFailure();
                            mySqlOptionBuilder.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                            mySqlOptionBuilder.EnableStringComparisonTranslations();
                        }));
            }
            builder.Services.AddPooledDbContextFactory<InMemoryDbContext>(options =>
            {
                options.UseInMemoryDatabase("default", (inMemoryDbOptions) =>
                {

                });
            }, 2048);
        }

        public override async Task RunAsync(CancellationToken cancellationToken = default)
        {
            Environment.CurrentDirectory = AppContext.BaseDirectory;

            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", _options.env);

                var builder = WebApplication.CreateBuilder(_args);

                // Add services to the container.

                Configure(builder);

                using var app = builder.Build();

                // Configure the HTTP request pipeline.
                await ConfigureAsync(app);

                await app.RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }
    }
}
