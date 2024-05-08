// add a reference to System.ComponentModel.DataAnnotations DLL

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Sqlite.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Debug;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure.Internal;

namespace NodeService.WebServer.Data;

public enum DatabaseProviderType
{
    Unknown,
    MySql,
    SqlServer,
    Sqlite
}

public partial class ApplicationDbContext : DbContext
{
    public static readonly LoggerFactory DebugLoggerFactory = new(new[]
    {
        new DebugLoggerProvider()
    });

    readonly Dictionary<Type, object> _dbSetMapping;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
        foreach (var extension in options.Extensions)
        {
            if (!extension.Info.IsDatabaseProvider) continue;
            if (extension is MySqlOptionsExtension)
                ProviderType = DatabaseProviderType.MySql;
            else if (extension is SqlServerOptionsExtension)
                ProviderType = DatabaseProviderType.SqlServer;
            else if (extension is SqliteOptionsExtension) ProviderType = DatabaseProviderType.Sqlite;
        }

        _dbSetMapping = new Dictionary<Type, object>();
        _dbSetMapping.Add(typeof(FtpConfigModel), FtpConfigurationDbSet);
        _dbSetMapping.Add(typeof(FtpUploadConfigModel), FtpUploadConfigurationDbSet);
        _dbSetMapping.Add(typeof(FtpDownloadConfigModel), FtpDownloadConfigurationDbSet);
        _dbSetMapping.Add(typeof(MysqlConfigModel), MysqlConfigurationDbSet);
        _dbSetMapping.Add(typeof(KafkaConfigModel), KafkaConfigurationDbSet);
        _dbSetMapping.Add(typeof(NodeEnvVarsConfigModel), NodeEnvVarsConfigurationDbSet);
        _dbSetMapping.Add(typeof(JobScheduleConfigModel), JobScheduleConfigurationDbSet);
        _dbSetMapping.Add(typeof(JobTypeDescConfigModel), JobTypeDescConfigurationDbSet);
        _dbSetMapping.Add(typeof(PackageConfigModel), PackageConfigurationDbSet);
        _dbSetMapping.Add(typeof(RestApiConfigModel), RestApiConfigurationDbSet);
        _dbSetMapping.Add(typeof(NotificationConfigModel), NotificationConfigurationsDbSet);
        _dbSetMapping.Add(typeof(WindowsTaskConfigModel), WindowsTaskConfigurationDbSet);
    }


    protected ApplicationDbContext(DbContextOptions contextOptions)
        : base(contextOptions)
    {
    }

    public DatabaseProviderType ProviderType { get; }

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


    public DbSet<FtpDownloadConfigModel> FtpDownloadConfigurationDbSet { get; set; }

    public DbSet<PackageConfigModel> PackageConfigurationDbSet { get; set; }

    public DbSet<RestApiConfigModel> RestApiConfigurationDbSet { get; set; }

    public DbSet<NodeEnvVarsConfigModel> NodeEnvVarsConfigurationDbSet { get; set; }

    public DbSet<JobFireConfigurationModel> JobFireConfigurationsDbSet { get; set; }


    public DbSet<FileRecordModel> FileRecordsDbSet { get; set; }

    public DbSet<Dictionary<string, object>> PropertyBagDbSet => Set<Dictionary<string, object>>("PropertyBag");

    public DbSet<NotificationConfigModel> NotificationConfigurationsDbSet { get; set; }

    public DbSet<NotificationRecordModel> NotificationRecordsDbSet { get; set; }

    public DbSet<ClientUpdateCounterModel> ClientUpdateCountersDbSet { get; set; }

    public DbSet<WindowsTaskConfigModel> WindowsTaskConfigurationDbSet { get; set; }

    static string Serialize<T>(T? value)
    {
        if (value == null) return string.Empty;
        return JsonSerializer.Serialize(value);
    }

    static T Deserialize<T>(string value) where T : class, new()
    {
        if (string.IsNullOrEmpty(value)) return new T();
        return JsonSerializer.Deserialize<T>(value);
    }

    static ValueComparer<IEnumerable<T>> GetEnumerableComparer<T>()
    {
        var comparer = new ValueComparer<IEnumerable<T>>(
            (r, l) => r.SequenceEqual(l),
            x => x.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            x => x
        );
        return comparer;
    }

    static ValueComparer<T> GetTypedComparer<T>() where T : IEquatable<T>
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
        if (!optionsBuilder.IsConfigured)
            if (Debugger.IsAttached)
                optionsBuilder.UseLoggerFactory(DebugLoggerFactory);
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
        switch (ProviderType)
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
        }
    }


    public DbSet<TEntity>? GetDbSet<TEntity>()
        where TEntity : class
    {
        if (_dbSetMapping.TryGetValue(typeof(TEntity), out var value)) return value as DbSet<TEntity>;
        return null;
    }
}