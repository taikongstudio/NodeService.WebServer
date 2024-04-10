// add a reference to System.ComponentModel.DataAnnotations DLL
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Debug;


namespace NodeService.WebServer.Data
{
    public enum DatabaseProviderType
    {
        Unknown,
        MySql,
        SqlServer,
        Sqlite,
    }

    public partial class ApplicationDbContext : DbContext
    {
        public DatabaseProviderType ProviderType { get; private set; }

        private readonly Dictionary<Type, object> _dbSetMapping;

        public static readonly LoggerFactory DebugLoggerFactory = new(new[] {
            new DebugLoggerProvider()
        });

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
            foreach (var extension in options.Extensions)
            {
                if (!extension.Info.IsDatabaseProvider)
                {
                    continue;
                }
                if (extension is Pomelo.EntityFrameworkCore.MySql.Infrastructure.Internal.MySqlOptionsExtension)
                {
                    this.ProviderType = DatabaseProviderType.MySql;
                }
                else if (extension is Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal.SqlServerOptionsExtension)
                {
                    this.ProviderType = DatabaseProviderType.SqlServer;
                }
                else if (extension is Microsoft.EntityFrameworkCore.Sqlite.Infrastructure.Internal.SqliteOptionsExtension)
                {
                    this.ProviderType = DatabaseProviderType.Sqlite;
                }

            }
            this._dbSetMapping = new Dictionary<Type, object>();
            this._dbSetMapping.Add(typeof(FtpConfigModel), FtpConfigurationDbSet);
            this._dbSetMapping.Add(typeof(FtpUploadConfigModel), FtpUploadConfigurationDbSet);
            this._dbSetMapping.Add(typeof(FtpDownloadConfigModel), FtpDownloadConfigurationDbSet);
            this._dbSetMapping.Add(typeof(MysqlConfigModel), MysqlConfigurationDbSet);
            this._dbSetMapping.Add(typeof(KafkaConfigModel), KafkaConfigurationDbSet);
            this._dbSetMapping.Add(typeof(NodeEnvVarsConfigModel), NodeEnvVarsConfigurationDbSet);
            this._dbSetMapping.Add(typeof(JobScheduleConfigModel), JobScheduleConfigurationDbSet);
            this._dbSetMapping.Add(typeof(JobTypeDescConfigModel), JobTypeDescConfigurationDbSet);
            this._dbSetMapping.Add(typeof(PackageConfigModel), PackageConfigurationDbSet);
            this._dbSetMapping.Add(typeof(RestApiConfigModel), RestApiConfigurationDbSet);
            this._dbSetMapping.Add(typeof(LogUploadConfigModel), LogUploadConfigurationDbSet);
            this._dbSetMapping.Add(typeof(NotificationConfigModel), NotificationConfigurationsDbSet);
        }


        protected ApplicationDbContext(DbContextOptions contextOptions)
            : base(contextOptions)
        {

        }

        public DbSet<ClientUpdateConfigModel> ClientUpdateConfigurationDbSet { get; set; }

        public DbSet<NodeInfoModel> NodeInfoDbSet { get; set; }

        public DbSet<NodeProfileModel> NodeProfilesDbSet { get; set; }

        public DbSet<JobExecutionInstanceModel> JobExecutionInstancesDbSet { get; set; }
        public DbSet<JobScheduleConfigModel> JobScheduleConfigurationDbSet { get; set; }

        public DbSet<NodePropertySnapshotModel> NodePropertiesSnapshotsDbSet { get; set; }

        public DbSet<JobTypeDescConfigModel> JobTypeDescConfigurationDbSet { get; set; }

        public DbSet<FtpConfigModel> FtpConfigurationDbSet { get; set; }

        public DbSet<KafkaConfigModel> KafkaConfigurationDbSet { get; set; }

        public DbSet<MysqlConfigModel> MysqlConfigurationDbSet { get; set; }

        public DbSet<FtpUploadConfigModel> FtpUploadConfigurationDbSet { get; set; }

        public DbSet<LogUploadConfigModel> LogUploadConfigurationDbSet { get; set; }

        public DbSet<FtpDownloadConfigModel> FtpDownloadConfigurationDbSet { get; set; }

        public DbSet<PackageConfigModel> PackageConfigurationDbSet { get; set; }

        public DbSet<RestApiConfigModel> RestApiConfigurationDbSet { get; set; }

        public DbSet<NodeEnvVarsConfigModel> NodeEnvVarsConfigurationDbSet { get; set; }

        public DbSet<JobFireConfigurationModel> JobFireConfigurationsDbSet { get; set; }


        public DbSet<FileRecordModel> FileRecordsDbSet { get; set; }

        public DbSet<Dictionary<string, object>> PropertyBagDbSet => Set<Dictionary<string, object>>("PropertyBag");

        public DbSet<NotificationConfigModel> NotificationConfigurationsDbSet { get; set; }

        public DbSet<NotificationRecordModel> NotificationRecordsDbSet { get; set; }


        private static string Serialize<T>(T? value)
        {
            if (value == null)
            {
                return string.Empty;
            }
            return JsonSerializer.Serialize(value, default(JsonSerializerOptions));
        }

        private static T Deserialize<T>(string value) where T : class, new()
        {
            if (string.IsNullOrEmpty(value))
            {
                return new T();
            }
            return JsonSerializer.Deserialize<T>(value, default(JsonSerializerOptions));
        }

        private static ValueComparer<IEnumerable<T>> GetEnumerableComparer<T>()
        {
            var comparer = new ValueComparer<IEnumerable<T>>(
                 (r, l) => r.SequenceEqual(l),
                 x => x.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                 x => x
                 );
            return comparer;
        }

        private static ValueComparer<T> GetTypedComparer<T>() where T : IEquatable<T>
        {
            var comparer = new ValueComparer<T>(
                 (r, l) => r.Equals(l),
                 x => x.GetHashCode(),
                 x => x
                 );
            return comparer;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (Debugger.IsAttached)
            {
                //optionsBuilder.EnableSensitiveDataLogging();
            }
            if (!optionsBuilder.IsConfigured)
            {
                if (Debugger.IsAttached)
                {
                    optionsBuilder.UseLoggerFactory(DebugLoggerFactory);
                }
            }
            base.OnConfiguring(optionsBuilder);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);


            modelBuilder.SharedTypeEntity<Dictionary<string, object>>("PropertyBag", builder =>
            {
                builder.IndexerProperty<string>("Id").IsRequired();
                builder.IndexerProperty<DateTime>("CreatedDateTime");
                builder.IndexerProperty<DateTime>("ModifiedDateTime");
                builder.IndexerProperty<string>("Value");
            });
            switch (this.ProviderType)
            {
                case DatabaseProviderType.Unknown:
                    break;
                case DatabaseProviderType.MySql:
                    MySql_BuildModels(modelBuilder);
                    break;
                case DatabaseProviderType.SqlServer:
                    SqlServer_BuildModels(modelBuilder);
                    break;
                case DatabaseProviderType.Sqlite:
                    break;
                default:
                    break;
            }


        }


        public DbSet<TEntity>? GetDbSet<TEntity>()
                where TEntity : class
        {
            if (this._dbSetMapping.TryGetValue(typeof(TEntity), out var value))
            {
                return value as DbSet<TEntity>;
            }
            return null;
        }

    }
}
