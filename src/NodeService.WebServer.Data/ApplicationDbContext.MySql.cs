namespace NodeService.WebServer.Data;

public partial class ApplicationDbContext
{
    private void MySql_BuildModels(ModelBuilder modelBuilder)
    {
        MySql_BuildClientUpdateInfoModel(modelBuilder);
        MySql_BuildJsonBasedDataModels(modelBuilder);
        MySql_BuildNodeInfoModel(modelBuilder);
        MySql_BuildNodeProfileModel(modelBuilder);
        MySql_BuildNodeStatusChangeRecordModel(modelBuilder);
        MySql_BuildNodePropertySnapshotModel(modelBuilder);
        MySql_BuildTaskExecutionInstanceModel(modelBuilder);
        MySql_BuildFileRecordModel(modelBuilder);
        Mysql_BuildNodeStatisticsRecordModel(modelBuilder);
        MySql_BuildNotificationRecordModel(modelBuilder);
        MySql_BuildJobFireConfigurationModel(modelBuilder);
        MySql_BuildClientUpdateCounterModel(modelBuilder);
    }

    private void Mysql_BuildNodeStatisticsRecordModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DataQualityNodeStatisticsRecordModel>()
            .HasKey(t => t.Id);

        modelBuilder.Entity<DataQualityNodeStatisticsRecordModel>(builder =>
        {
            builder.Property(x => x.Value)
                .HasColumnType("json").HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                    v => JsonSerializer.Deserialize<DataQualityNodeStatisticsRecord>(v, (JsonSerializerOptions)null));
        });
    }

    private void MySql_BuildNodeStatusChangeRecordModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NodeStatusChangeRecordModel>()
            .HasKey(t => t.Id);
    }

    private void MySql_BuildClientUpdateCounterModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClientUpdateCounterModel>()
            .HasKey(t => new { t.Id, t.Name });

        modelBuilder.Entity<ClientUpdateCounterModel>()
            .Property(x => x.Counters)
            .HasConversion(x => Serialize(x), x => Deserialize<List<ClientUpdateCategoryModel>>(x))
            .Metadata
            .SetValueComparer(GetEnumerableComparer<ClientUpdateCategoryModel>());
    }

    private void MySql_BuildNotificationRecordModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationRecordModel>(entityBuilder =>
        {
            entityBuilder.HasKey(nameof(NotificationRecordModel.Id));
        });
    }

    private void MySql_BuildJobFireConfigurationModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TaskActivationRecordModel>(entityBuilder =>
        {
            entityBuilder.HasKey(nameof(TaskActivationRecordModel.Id));
        });
    }

    private void MySql_BuildFileRecordModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileRecordModel>()
            .HasKey(t => new { t.Id, t.Name });
    }

    private void MySql_BuildClientUpdateInfoModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClientUpdateConfigModel>()
            .HasKey(t => t.Id);

        modelBuilder.Entity<ClientUpdateConfigModel>()
            .Property(x => x.DnsFilters)
            .HasConversion(x => Serialize(x), x => Deserialize<List<StringEntry>>(x))
            .Metadata
            .SetValueComparer(GetEnumerableComparer<StringEntry>());

        modelBuilder.Entity<ClientUpdateConfigModel>()
            .Navigation(x => x.PackageConfig)
            .AutoInclude();
    }


    private static void MySql_BuildTaskExecutionInstanceModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TaskExecutionInstanceModel>()
            .HasKey(nameof(TaskExecutionInstanceModel.Id));

        modelBuilder.Entity<TaskExecutionInstanceModel>()
            .Navigation(x => x.NodeInfo)
            .AutoInclude(false);
    }

    private void MySql_BuildNodePropertySnapshotModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NodePropertySnapshotModel>()
            .HasKey(nameof(NodePropertySnapshotModel.Id));

        modelBuilder.Entity<NodePropertySnapshotModel>()
            .Property(x => x.NodeProperties)
            .HasColumnType("json")
            .HasConversion(x => Serialize(x), x => Deserialize<List<NodePropertyEntry>>(x))
            .Metadata
            .SetValueComparer(GetEnumerableComparer<NodePropertyEntry>());
    }

    private static void MySql_BuildNodeProfileModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NodeProfileModel>()
            .HasKey(nameof(NodeProfileModel.Id));
    }

    private static void MySql_BuildNodeInfoModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NodeInfoModel>()
            .HasKey(nameof(NodeInfoModel.Id));

        modelBuilder.Entity<NodeInfoModel>()
            .HasMany(x => x.TaskExecutionInstances)
            .WithOne(x => x.NodeInfo)
            .HasForeignKey(x => x.NodeInfoId)
            .IsRequired();

        modelBuilder.Entity<NodeInfoModel>()
            .HasOne(x => x.Profile)
            .WithOne()
            .HasForeignKey<NodeInfoModel>(x => x.ProfileId)
            .IsRequired();


        modelBuilder.Entity<NodeInfoModel>()
            .Navigation(x => x.Profile)
            .AutoInclude();
    }
    
    private static void MySql_BuildJsonBasedDataModels(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FtpConfigModel>(builder =>
        {
            builder.Property(x => x.Value)
                .HasColumnType("json").HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                    v => JsonSerializer.Deserialize<FtpConfiguration>(v, (JsonSerializerOptions)null));
        });

        modelBuilder.Entity<NodeEnvVarsConfigModel>(builder =>
        {
            builder.Property(x => x.Value)
                .HasColumnType("json").HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                    v => JsonSerializer.Deserialize<NodeEnvVarsConfiguration>(v, (JsonSerializerOptions)null));
        });

        modelBuilder.Entity<FtpDownloadConfigModel>(builder =>
        {
            builder.Property(x => x.Value)
                .HasColumnType("json").HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                    v => JsonSerializer.Deserialize<FtpDownloadConfiguration>(v, (JsonSerializerOptions)null));
        });

        modelBuilder.Entity<FtpUploadConfigModel>(builder =>
        {
            builder.Property(x => x.Value)
                .HasColumnType("json").HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                    v => JsonSerializer.Deserialize<FtpUploadConfiguration>(v, (JsonSerializerOptions)null));
        });

        modelBuilder.Entity<KafkaConfigModel>(builder =>
        {
            builder.Property(x => x.Value)
                .HasColumnType("json").HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                    v => JsonSerializer.Deserialize<KafkaConfiguration>(v, (JsonSerializerOptions)null));
        });

        modelBuilder.Entity<DatabaseConfigModel>(builder =>
        {
            builder.Property(x => x.Value)
                .HasColumnType("json").HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                    v => JsonSerializer.Deserialize<DatabaseConfiguration>(v, (JsonSerializerOptions)null));
        });

        modelBuilder.Entity<TaskDefinitionModel>(builder =>
        {
            builder.Property(x => x.Value)
                .HasColumnType("json").HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                    v => JsonSerializer.Deserialize<TaskDefinition>(v, (JsonSerializerOptions)null));
        });

        modelBuilder.Entity<TaskTypeDescConfigModel>(builder =>
        {
            builder.Property(x => x.Value)
                .HasColumnType("json").HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                    v => JsonSerializer.Deserialize<TaskTypeDescConfiguration>(v, (JsonSerializerOptions)null));
        });

        modelBuilder.Entity<RestApiConfigModel>(builder =>
        {
            builder.Property(x => x.Value)
                .HasColumnType("json").HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                    v => JsonSerializer.Deserialize<RestApiConfiguration>(v, (JsonSerializerOptions)null));
        });

        modelBuilder.Entity<PackageConfigModel>(builder =>
        {
            builder.Property(x => x.Value)
                .HasColumnType("json").HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                    v => JsonSerializer.Deserialize<PackageConfiguration>(v, (JsonSerializerOptions)null));
        });

        modelBuilder.Entity<NotificationConfigModel>(builder =>
        {
            builder.Property(x => x.Value)
                .HasColumnType("json").HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                    v => JsonSerializer.Deserialize<NotificationConfiguration>(v, (JsonSerializerOptions)null));
        });

        modelBuilder.Entity<WindowsTaskConfigModel>(builder =>
        {
            builder.Property(x => x.Value)
                .HasColumnType("json").HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                    v => JsonSerializer.Deserialize<WindowsTaskDefintionConfiguration>(v, (JsonSerializerOptions)null));
        });

        modelBuilder.Entity<DataQualityStatisticsDefinitionModel>(builder =>
        {
            builder.Property(x => x.Value)
                .HasColumnType("json").HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                    v => JsonSerializer.Deserialize<DataQualityStatisticsDefinition>(v, (JsonSerializerOptions)null));
        });

        modelBuilder.Entity<FileSystemWatchConfigModel>(builder =>
        {
            builder.Property(x => x.Value)
                .HasColumnType("json").HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                    v => JsonSerializer.Deserialize<FileSystemWatchConfiguration>(v, (JsonSerializerOptions)null));
        });

        modelBuilder.Entity<TaskLogModel>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Value)
                .HasColumnType("json").HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                    v => JsonSerializer.Deserialize<TaskLog>(v, (JsonSerializerOptions)null));
        });
    }
}