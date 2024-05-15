namespace NodeService.WebServer.Data;

public partial class ApplicationDbContext
{
    private void MySql_BuildModels(ModelBuilder modelBuilder)
    {
        MySql_BuildClientUpdateInfoModel(modelBuilder);
        MySql_BuildConfigurationModels(modelBuilder);
        MySql_BuildNodeInfoModel(modelBuilder);
        MySql_BuildNodeProfileModel(modelBuilder);
        MySql_BuildNodePropertySnapshotModel(modelBuilder);
        MySql_BuildJobExecutionInstanceModel(modelBuilder);
        MySql_BuildFileUploadRecordModel(modelBuilder);
        MySql_BuildNotificationRecordModel(modelBuilder);
        MySql_BuildJobFireConfigurationModel(modelBuilder);
        MySql_BuildClientUpdateCounterModel(modelBuilder);
    }

    private void MySql_BuildClientUpdateCounterModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClientUpdateCounterModel>()
            .HasKey(t => new { t.Id, t.Name });

        modelBuilder.Entity<ClientUpdateCounterModel>()
            .Property(x => x.Counters)
            .HasConversion(x => Serialize(x), x => Deserialize<List<CategoryModel>>(x))
            .Metadata
            .SetValueComparer(GetEnumerableComparer<CategoryModel>());
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
        modelBuilder.Entity<JobFireConfigurationModel>(entityBuilder =>
        {
            entityBuilder.HasKey(nameof(JobFireConfigurationModel.Id));
        });
    }

    private void MySql_BuildFileUploadRecordModel(ModelBuilder modelBuilder)
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


    private static void MySql_BuildJobExecutionInstanceModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JobExecutionInstanceModel>()
            .HasKey(nameof(JobExecutionInstanceModel.Id));

        modelBuilder.Entity<JobExecutionInstanceModel>()
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
            .HasMany(x => x.JobExecutionInstances)
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

    private static void MySql_BuildConfigurationModels(ModelBuilder modelBuilder)
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

        modelBuilder.Entity<MysqlConfigModel>(builder =>
        {
            builder.Property(x => x.Value)
                .HasColumnType("json").HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                    v => JsonSerializer.Deserialize<MysqlConfiguration>(v, (JsonSerializerOptions)null));
        });

        modelBuilder.Entity<JobScheduleConfigModel>(builder =>
        {
            builder.Property(x => x.Value)
                .HasColumnType("json").HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                    v => JsonSerializer.Deserialize<JobScheduleConfiguration>(v, (JsonSerializerOptions)null));
        });

        modelBuilder.Entity<JobTypeDescConfigModel>(builder =>
        {
            builder.Property(x => x.Value)
                .HasColumnType("json").HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                    v => JsonSerializer.Deserialize<JobTypeDescConfiguration>(v, (JsonSerializerOptions)null));
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
    }
}