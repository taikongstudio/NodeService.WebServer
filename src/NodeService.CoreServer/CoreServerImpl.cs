using AntDesign.ProLayout;
using CurrieTechnologies.Razor.Clipboard;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Identity;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Auth;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.DataQuality;
using NodeService.WebServer.Services.DataQueue;
using NodeService.WebServer.Services.Dianostics;
using NodeService.WebServer.Services.NetworkDevices;
using NodeService.WebServer.Services.NodeFileSystem;
using NodeService.WebServer.Services.NodeSessions;
using NodeService.WebServer.Services.NodeSessions.MessageHandlers;
using NodeService.WebServer.Services.Notifications;
using NodeService.WebServer.Services.Tasks;
using NodeService.WebServer.Services.VirtualFileSystem;
using NodeService.WebServer.UI.Services;
using OpenTelemetry.Metrics;
using Quartz.Spi;
using StackExchange.Redis;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using System.Xml.Linq;
using WatchDog;

namespace NodeService.CoreServer
{
    public class CoreServerImpl
    {
        readonly CommandLineOptions _options;
        readonly string[] _args;

        public CoreServerImpl(CommandLineOptions options, [FromKeyedServices(nameof(args))] string[] args)
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

            MapGrpcServices(app);

            //app.UseHttpsRedirection();
            app.UseHsts();

            app.UseStaticFiles();

            app.UseFileServer(new FileServerOptions()
            {
                EnableDirectoryBrowsing = true,
                RequestPath = "/NodeFileSystem/Nodes",
                FileProvider = new NodeFileProvider(),
            });

            app.UseRouting();

            app.UseCors("AllowAll");

            // app.MapDynamicControllerRoute<MyDynamicRouteValueTransformer>("api/{controller}/{action}/{**path}");
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapRazorPages();
            app.MapControllers();
            app.UseRateLimiter();
            app.MapBlazorHub(options =>
            {
                options.CloseOnAuthenticationExpiration = true;
                //This option is used to enable authentication expiration tracking which will close connections when a token expires  
            });
            app.MapFallbackToPage("/_Host");
            app.MapPrometheusScrapingEndpoint();

            var factory = app.Services.GetService<IDbContextFactory<ApplicationDbContext>>();
            await using var dbContext = await factory.CreateDbContextAsync();
            dbContext.Database.EnsureCreated();

            using var scope = app.Services.CreateAsyncScope();
            using var applicationUserDbContext = scope.ServiceProvider.GetService<ApplicationUserDbContext>();
            applicationUserDbContext.Database.EnsureCreated();
        }

        void MapGrpcServices(WebApplication app)
        {
            app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
            //app.MapGrpcService<FileSystemServiceImpl>().EnableGrpcWeb().RequireCors("AllowAll");
            app.MapGrpcService<NodeServerImpl>().EnableGrpcWeb().RequireCors("AllowAll");
        }

        void Configure(WebApplicationBuilder builder)
        {
            ConfigureOptions(builder);

            builder.Services.AddRequestDecompression(options =>
            {

            });
            builder.Services.AddControllersWithViews();
            builder.Services.AddControllers();
            builder.WebHost.ConfigureKestrel(options => { options.Limits.MaxRequestLineSize = 8192 * 10; });

            builder.Services.AddRazorPages(options =>
            {
                options.Conventions.AllowAnonymousToAreaFolder("Identity", "/Account");
            });

            builder.Services.AddServerSideBlazor(options => { options.DetailedErrors = true; });
            builder.Services.AddAntDesign();
            builder.Services.AddOpenApiDocument();
            builder.Services.AddHttpClient();
            builder.Services.AddMemoryCache(options => { options.TrackStatistics = true; });
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

            builder.Services.AddDataQueueService<FileRecordModel>();

            ConfigureDbContext(builder);

            builder.Services.AddLogging(logger =>
            {
                logger.AddFilter("Microsoft.AspNetCore.Components.RenderTree.*", LogLevel.None);
                logger.ClearProviders();
                logger.AddConsole();
                logger.AddNLog();
            });

            ConfigureSingleton(builder);

            ConfigureAuthentication(builder);

            ConfigureScoped(builder);

            ConfigureHostedServices(builder);

            ConfigureGrpc(builder);

            ConfifureCor(builder);

            ConfigureRateLimiter(builder);
        }

        private static void ConfigureOptions(WebApplicationBuilder builder)
        {
            builder.Services.Configure<WebServerOptions>(builder.Configuration.GetSection(nameof(WebServerOptions)));
            builder.Services.Configure<RedisOptions>(builder.Configuration.GetSection(nameof(RedisOptions)));
            builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection(nameof(KafkaOptions)));
            builder.Services.Configure<FtpOptions>(builder.Configuration.GetSection(nameof(FtpOptions)));
            builder.Services.Configure<MongoDbOptions>(builder.Configuration.GetSection(nameof(MongoDbOptions)));
            builder.Services.Configure<ProSettings>(builder.Configuration.GetSection(nameof(ProSettings)));
            builder.Services.Configure<FormOptions>(options => { options.MultipartBodyLengthLimit = 1024 * 1024 * 1024 * 4L; });
        }

        void ConfigureGrpc(WebApplicationBuilder builder)
        {
            builder.Services.AddGrpc(grpcServiceOptions =>
            {
                grpcServiceOptions.MaxReceiveMessageSize = null;
            });
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
            builder.Services.AddRateLimiter(options => options
                .AddConcurrencyLimiter("PackageDownloadConcurrency", options =>
                {
                    options.PermitLimit = 1;
                    options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    options.QueueLimit = 10000;
                }));

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
            builder.Services.AddHostedService<TaskScheduleService>();
            builder.Services.AddHostedService<TaskExecutionReportConsumerService>();
            builder.Services.AddHostedService<HeartBeatResponseConsumerService>();
            builder.Services.AddHostedService<HeartBeatRequestProducerService>();
            builder.Services.AddHostedService<TaskLogPersistenceService>();
            builder.Services.AddHostedService<NotificationService>();
            builder.Services.AddHostedService<NodeHealthyCheckService>();
            builder.Services.AddHostedService<TaskActivateService>();
            builder.Services.AddHostedService<NodeStatusChangeRecordService>();
            builder.Services.AddHostedService<DataQualityStatisticsService>();
            builder.Services.AddHostedService<DataQualityAlarmService>();
            builder.Services.AddHostedService<ClientUpdateQueryQueueService>();
            builder.Services.AddHostedService<TaskCancellationQueueService>();
            builder.Services.AddHostedService<NodeConfigurationChangedNotifyService>();
            builder.Services.AddHostedService<NetworkDeviceScanService>();
            builder.Services.AddHostedService<PackageQueryQueueService>();
            builder.Services.AddHostedService<GCService>();
            builder.Services.AddHostedService<TaskLogKafkaProducerService>();
            builder.Services.AddHostedService<TaskLogKafkaConsumerService>();
        }

        void ConfigureScoped(WebApplicationBuilder builder)
        {
            builder.Services.AddScoped(sp => new HttpClient
            {
                BaseAddress = new Uri(builder.Configuration.GetValue<string>("Kestrel:Endpoints:MyHttpEndpoint:Url"))
            });

            builder.Services.AddScoped<IBackendApiHttpClient, BackendApiHttpClient>();


            builder.Services.AddScoped<AuthenticationStateProvider, ApiAuthenticationStateProvider>();

            builder.Services.AddScoped(serviceProvider =>
            {
                var optionSnapshot = serviceProvider.GetService<IOptionsSnapshot<FtpOptions>>();
                var ftpServerConfig = optionSnapshot.Value;
                return new AsyncFtpClient(ftpServerConfig.Host,
                    ftpServerConfig.Username,
                    ftpServerConfig.Password,
                    ftpServerConfig.Port);
            });

            //builder.Services.AddScoped(serviceProvider =>
            //{
            //    var httpsEndpointUrl = builder.Configuration.GetValue<string>("Kestrel:Endpoints:MyHttpsEndpoint:Url");
            //    var requestUri = new Uri(httpsEndpointUrl);
            //    var handler = new HttpClientHandler();
            //    handler.ServerCertificateCustomValidationCallback =
            //        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            //    var channel = GrpcChannel.ForAddress(requestUri, new GrpcChannelOptions
            //    {
            //        HttpHandler = handler,
            //        Credentials = ChannelCredentials.SecureSsl
            //    });
            //    return new FileSystem.FileSystemClient(channel);
            //});

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

            builder.Services.AddScoped<IChartService, ChartService>();
            builder.Services.AddScoped<IProjectService, ProjectService>();
            builder.Services.AddScoped<IUserService, UserService>();
            builder.Services.AddScoped<IAccountService, AccountService>();
            builder.Services.AddScoped<IProfileService, ProfileService>();
            builder.Services.AddClipboard();

            builder.Services.AddScoped(serviceProvider =>
            {
                var httpClient = serviceProvider.GetService<HttpClient>();
                return new ApiService(httpClient);
            });

            builder.Services.AddScoped<HeartBeatResponseHandler>();
            builder.Services.AddScoped<TaskExecutionReportHandler>();
            builder.Services.AddScoped(sp =>
            {
                var messageHandlerDictionary = new MessageHandlerDictionary
                {
                    { HeartBeatResponse.Descriptor, sp.GetService<HeartBeatResponseHandler>() },
                    { TaskExecutionReport.Descriptor, sp.GetService<TaskExecutionReportHandler>() }
                    //{ FileSystemBulkOperationReport.Descriptor, sp.GetService<FileSystemOperationReportHandler>() },
                };
                return messageHandlerDictionary;
            }
            );
        }

        void ConfigureAuthentication(WebApplicationBuilder builder)
        {
            builder.Services.AddCascadingAuthenticationState();

            builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(t =>
            {
                t.Password.RequireDigit = false;
                t.Password.RequireNonAlphanumeric = false;
                t.Password.RequireUppercase = false;
                t.Password.RequireLowercase = false;
                t.Password.RequiredLength = 6;
            }).AddEntityFrameworkStores<ApplicationUserDbContext>().AddDefaultTokenProviders();

            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
            builder.Services.AddAuthentication(option =>
            {
                option.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                option.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                option.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(cfg =>
            {
                cfg.RequireHttpsMetadata = false;
                cfg.SaveToken = true;
                cfg.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = builder.Configuration.GetValue<string>("JWTSettings:Issuer"),
                    ValidAudience = builder.Configuration.GetValue<string>("JWTSettings:Issuer"),
                    IssuerSigningKey =
                        new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(builder.Configuration.GetValue<string>("JWTSettings:Secret"))),
                    ClockSkew = TimeSpan.Zero
                };
                cfg.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        if (context.Exception.GetType() == typeof(SecurityTokenExpiredException))
                            context.Response.Headers.Append("Token-Expired", "true");

                        return Task.CompletedTask;
                    }
                };
            });

            builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JWTSettings"));
            builder.Services.AddScoped<IAccessControlService, AccessControlService>();
            builder.Services.AddScoped<LoginService>();
        }

        void ConfigureSingleton(WebApplicationBuilder builder)
        {
            //builder.Services.AddSingleton<MyDynamicRouteValueTransformer>();
            builder.Services.AddSingleton<NodeFileSyncQueueDictionary>();
            builder.Services.AddSingleton<FtpClientFactory>();
            builder.Services.AddStackExchangeRedisCache(options =>
            {
                var endPointsString = builder.Configuration.GetValue<string>("RedisOptions:EndPoints");
                var password = builder.Configuration.GetValue<string>("RedisOptions:Password");
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
                options.InstanceName = "NodeService";
                options.ConfigurationOptions = new ConfigurationOptions()
                {

                    EndPoints = endPoints,
                    Password = password,
                };
            });
            builder.Services.AddSingleton<ObjectCache>();

            builder.Services.AddSingleton(sp =>
            {
                var connectionString = builder.Configuration.GetValue<string>("MongoDbOptions:ConnectionString");
                return new MongoClient();
            });

            builder.Services.AddSingleton(sp =>
            {
                var dbName = builder.Configuration.GetValue<string>("MongoDbOptions:TaskLogDatabaseName");
                var client = sp.GetService<MongoClient>();
                var mongoServer = client.GetServer();
                var mongoGridFS = new MongoGridFS(mongoServer, dbName, new MongoGridFSSettings()
                {
                    UpdateMD5 = false
                });
                return mongoGridFS;
            });

            builder.Services.AddSingleton(_options);
            builder.Services.AddSingleton<ExceptionCounter>();
            builder.Services.AddSingleton<WebServerCounter>();
            builder.Services.AddSingleton(typeof(ApplicationRepositoryFactory<>));

            builder.Services.AddSingleton<TaskLogQueryService>();
            builder.Services.AddSingleton<ConfigurationQueryService>();
            builder.Services.AddSingleton<SyncRecordQueryService>();
            builder.Services.AddSingleton<FileRecordQueryService>();
            builder.Services.AddSingleton<NodeInfoQueryService>();
            builder.Services.AddSingleton<FileInfoCacheService>();

            builder.Services.AddTaskSchedule(Options =>
            {

            });


            builder.Services.AddSingleton<IAsyncQueue<NotificationMessage>, AsyncQueue<NotificationMessage>>();
            builder.Services.AddSingleton<IAsyncQueue<ConfigurationChangedEvent>, AsyncQueue<ConfigurationChangedEvent>>();

            builder.Services.AddSingleton<INodeSessionService, NodeSessionService>();
            builder.Services.AddSingleton(new BatchQueue<NodeHeartBeatSessionMessage>(TimeSpan.FromSeconds(3), 2048));
            builder.Services.AddSingleton(new BatchQueue<NodeStatusChangeRecordModel>(TimeSpan.FromSeconds(3), 1024));
            builder.Services.AddSingleton<NodeHealthyCounterDictionary>();

            builder.Services.AddSingleton(new BatchQueue<DataQualityAlarmMessage>(TimeSpan.FromMinutes(30), 1024));
            builder.Services.AddSingleton(
                new BatchQueue<AsyncOperation<FileRecordModel, bool>>(TimeSpan.FromSeconds(1), 1024 * 2));
            builder.Services.AddSingleton(
                new BatchQueue<AsyncOperation<ClientUpdateBatchQueryParameters, ClientUpdateConfigModel>>(TimeSpan.FromSeconds(1),
                    64));

            builder.Services.AddSingleton(new BatchQueue<AsyncOperation<PackageDownloadParameters, PackageDownloadResult>>(TimeSpan.FromSeconds(5), 1024));

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
                {
                    options.UseMySql(builder.Configuration.GetConnectionString("NodeServiceDbMySQL"),
                        MySqlServerVersion.LatestSupportedServerVersion, mySqlOptionBuilder =>
                        {
                            mySqlOptionBuilder.EnableRetryOnFailure();
                            mySqlOptionBuilder.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                            mySqlOptionBuilder.EnableStringComparisonTranslations();
                        });
                }, 2048);
                builder.Services.AddDbContext<ApplicationUserDbContext>(options =>
                {
                    options.UseMySql(builder.Configuration.GetConnectionString("NodeServiceUserDbMySQL"),
                          MySqlServerVersion.LatestSupportedServerVersion, mySqlOptionBuilder =>
                          {
                              mySqlOptionBuilder.EnableRetryOnFailure();
                              mySqlOptionBuilder.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                              mySqlOptionBuilder.EnableStringComparisonTranslations();
                          });
                });
            }
            builder.Services.AddPooledDbContextFactory<InMemoryDbContext>(options =>
            {
                options.UseInMemoryDatabase("default", (inMemoryDbOptions) =>
                {

                });
            }, 2048);
        }

        public async Task RunAsync(CancellationToken cancellationToken = default)
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
