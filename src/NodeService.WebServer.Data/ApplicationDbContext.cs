// add a reference to System.ComponentModel.DataAnnotations DLL

using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Sqlite.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Debug;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure.Internal;
using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace NodeService.WebServer.Data;

public partial class ApplicationDbContext : DbContext
{
    public static readonly LoggerFactory DebugLoggerFactory = new(new[]
    {
        new DebugLoggerProvider()
    });

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
        foreach (var extension in options.Extensions)
        {
            if (!extension.Info.IsDatabaseProvider) continue;
            if (extension is MySqlOptionsExtension)
                ProviderType = DatabaseProviderType.MySql;
            else if (extension is SqlServerOptionsExtension)
                ProviderType = DatabaseProviderType.SqlServer;
            else if (extension is SqliteOptionsExtension)
                ProviderType = DatabaseProviderType.Sqlite;
        }
    }


    protected ApplicationDbContext(DbContextOptions contextOptions)
        : base(contextOptions)
    {
    }

    public DatabaseProviderType ProviderType { get; }

    public DbSet<ClientUpdateConfigModel> ClientUpdateConfigurationDbSet { get; set; }

    public DbSet<NodeInfoModel> NodeInfoDbSet { get; set; }

    public DbSet<NodeProfileModel> NodeProfilesDbSet { get; set; }

    public DbSet<TaskExecutionInstanceModel> TaskExecutionInstanceDbSet { get; set; }
    public DbSet<TaskDefinitionModel> TaskDefinitionDbSet { get; set; }

    public DbSet<NodePropertySnapshotModel> NodePropertiesSnapshotDbSet { get; set; }

    public DbSet<TaskTypeDescConfigModel> TaskTypeDescConfigurationDbSet { get; set; }

    public DbSet<FtpConfigModel> FtpConfigurationDbSet { get; set; }

    public DbSet<KafkaConfigModel> KafkaConfigurationDbSet { get; set; }

    public DbSet<DatabaseConfigModel> DatabaseConfigurationDbSet { get; set; }

    public DbSet<FtpUploadConfigModel> FtpUploadConfigurationDbSet { get; set; }


    public DbSet<FtpDownloadConfigModel> FtpDownloadConfigurationDbSet { get; set; }

    public DbSet<PackageConfigModel> PackageConfigurationDbSet { get; set; }

    public DbSet<RestApiConfigModel> RestApiConfigurationDbSet { get; set; }

    public DbSet<NodeEnvVarsConfigModel> NodeEnvVarsConfigurationDbSet { get; set; }

    public DbSet<TaskActivationRecordModel> TaskActivationRecordDbSet { get; set; }

    public DbSet<FileRecordModel> FileRecordDbSet { get; set; }

    public DbSet<PropertyBag> PropertyBagDbSet => Set<PropertyBag>(nameof(PropertyBag));

    public DbSet<NotificationConfigModel> NotificationConfigurationDbSet { get; set; }

    public DbSet<NotificationRecordModel> NotificationRecordDbSet { get; set; }

    public DbSet<ClientUpdateCounterModel> ClientUpdateCounterDbSet { get; set; }

    public DbSet<WindowsTaskConfigModel> WindowsTaskConfigurationDbSet { get; set; }

    public DbSet<NodeStatusChangeRecordModel> NodeStatusChangeRecordDbSet { get; set; }

    public DbSet<DataQualityStatisticsDefinitionModel> DataQualityStatisticsDefinitionDbSet { get; set; }

    public DbSet<DataQualityNodeStatisticsRecordModel> DataQualityNodeStatisticsReportDbSet { get; set; }

    public DbSet<FileSystemWatchConfigModel> FileSystemWatchConfigurationDbSet { get; set; }

    //public DbSet<TaskLogModel> TaskLogDbSet { get; set; }

    public DbSet<TaskLogModel> TaskLogStorageDbSet { get; set; }

    public DbSet<ConfigurationVersionRecordModel> ConfigurationVersionRecordDbSet { get; set; }

    public DbSet<TaskFlowTemplateModel> TaskFlowTemplateDbSet { get; set; }

    public DbSet<TaskFlowExecutionInstanceModel> TaskFlowExecutionInstanceDbSet { get; set; }

    public DbSet<NodeFileSyncRecordModel> NodeFileSyncRecordDbSet { get; set; }

    public DbSet<NodeFileSystemInfoModel> NodeFileSystemInfoDbSetDbSet { get; set; }

    public DbSet<NodeUsageConfigurationModel> NodeUsageConfigurationDbSet { get; set; }

    public override DbSet<TEntity> Set<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
                                    DynamicallyAccessedMemberTypes.NonPublicConstructors |
                                    DynamicallyAccessedMemberTypes.PublicFields |
                                    DynamicallyAccessedMemberTypes.NonPublicFields |
                                    DynamicallyAccessedMemberTypes.PublicProperties |
                                    DynamicallyAccessedMemberTypes.NonPublicProperties |
                                    DynamicallyAccessedMemberTypes.Interfaces)]
        TEntity>()
    {
        if (typeof(TEntity) == typeof(PropertyBag)) return base.Set<PropertyBag>("PropertyBag") as DbSet<TEntity>;
        return base.Set<TEntity>();
    }

    private static string Serialize<T>(T? value)
    {
        if (value == null) return string.Empty;
        return JsonSerializer.Serialize(value);
    }

    private static T Deserialize<T>(string value) where T : class, new()
    {
        if (string.IsNullOrEmpty(value)) return new T();
        return JsonSerializer.Deserialize<T>(value);
    }

    private static ValueComparer<IEnumerable<T>> GetEnumerableComparer<T>()
    {
        var comparer = new ValueComparer<IEnumerable<T>>(
            (r, l) => SequenceEqual(r, l),
            x => GetCollectionHashCode(x),
            x => Snapshot(x)
        );
        return comparer;
    }

    private static IEnumerable<T> Snapshot<T>(IEnumerable<T> source)
    {
        if (source is List<T>)
            return source.ToList();
        else if (source is T[])
            return source.ToArray();
        else if (source is IDictionary dictionary) return source;
        return source;
    }

    private static bool SequenceEqual<T>(IEnumerable<T> left, IEnumerable<T> right)
    {
        if (left == right)
        {
            return true;
        }
        return left.SequenceEqual(right);
    }

    private static int GetCollectionHashCode<T>(IEnumerable<T> collection)
    {
        return collection.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode()));
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
        if (!optionsBuilder.IsConfigured)
            if (Debugger.IsAttached)
                optionsBuilder.UseLoggerFactory(DebugLoggerFactory);
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);


        modelBuilder.SharedTypeEntity<PropertyBag>("PropertyBag", builder =>
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
}