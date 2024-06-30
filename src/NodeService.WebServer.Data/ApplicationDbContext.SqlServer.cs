using Microsoft.AspNetCore.Identity;
using NodeService.Infrastructure.Logging;

namespace NodeService.WebServer.Data;

public partial class ApplicationDbContext
{
    private void SqlServer_BuildModels(ModelBuilder modelBuilder)
    {
        SqlServer_BuildClientUpdateInfoModel(modelBuilder);
        SqlServer_BuildJsonBasedDataModels(modelBuilder);
        SqlServer_BuildTaskExecutionInstanceModel(modelBuilder);
        SqlServer_BuildNodeInfoModel(modelBuilder);
        SqlServer_BuildNodeProfileModel(modelBuilder);
        SqlServer_BuildNodeStatusChangeRecordModel(modelBuilder);
        SqlServer_BuildNodePropertySnapshotModel(modelBuilder);
        SqlServer_BuildFileRecordModel(modelBuilder);
        SqlServer_BuildNodeStatisticsRecordModel(modelBuilder);
        SqlServer_BuildNotificationRecordModel(modelBuilder);
        SqlServer_BuildTaskActivationRecordModel(modelBuilder);
        SqlServer_BuildClientUpdateCounterModel(modelBuilder);
    }

    private void SqlServer_BuildNodeStatisticsRecordModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DataQualityNodeStatisticsRecordModel>(builder =>
            builder.OwnsOne(
                model => model.Value, ownedNavigationBuilder =>
                {
                    ownedNavigationBuilder.ToJson();
                    ownedNavigationBuilder.Property(x => x.Entries)
                        .HasConversion(x => Serialize(x),
                            x => Deserialize<List<DataQualityNodeStatisticsEntry>>(x))
                        .Metadata
                        .SetValueComparer(GetEnumerableComparer<DataQualityNodeStatisticsEntry>());
                }));
    }

    private void SqlServer_BuildNodeStatusChangeRecordModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NodeStatusChangeRecordModel>()
            .HasKey(t => t.Id);
    }

    private void SqlServer_BuildClientUpdateCounterModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClientUpdateCounterModel>(entityBuilder =>
        {
            entityBuilder.HasKey(nameof(ClientUpdateCounterModel.Id));
            modelBuilder.Entity<ClientUpdateCounterModel>()
                .Property(x => x.Counters)
                .HasConversion(x => Serialize(x), x => Deserialize<List<ClientUpdateCategoryModel>>(x))
                .Metadata
                .SetValueComparer(GetEnumerableComparer<ClientUpdateCategoryModel>());
        });
    }

    private void SqlServer_BuildNotificationRecordModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationRecordModel>(entityBuilder =>
        {
            entityBuilder.HasKey(nameof(NotificationRecordModel.Id));
        });
    }

    private void SqlServer_BuildTaskActivationRecordModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TaskActivationRecordModel>(builder =>
        {
            builder.HasKey(nameof(TaskActivationRecordModel.Id));
            builder.OwnsOne(
                model => model.Value, ownedNavigationBuilder =>
                {
                    ownedNavigationBuilder.ToJson();
                    ownedNavigationBuilder
                                    .Property(x => x.TaskExecutionInstanceInfoList)
                                    .HasConversion(x => Serialize(x), x => Deserialize<List<TaskExecutionInstanceInfo>>(x))
                                    .Metadata
                                    .SetValueComparer(GetEnumerableComparer<TaskExecutionInstanceInfo>());
                });
        });
    }

    private void SqlServer_BuildFileRecordModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileRecordModel>()
            .HasKey(t => new { t.Id, t.Name });
    }

    private void SqlServer_BuildClientUpdateInfoModel(ModelBuilder modelBuilder)
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


    private static void SqlServer_BuildTaskExecutionInstanceModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TaskExecutionInstanceModel>()
            .HasKey(nameof(TaskExecutionInstanceModel.Id));

        modelBuilder.Entity<TaskExecutionInstanceModel>()
            .Navigation(x => x.NodeInfo)
            .AutoInclude();
    }

    private void SqlServer_BuildNodePropertySnapshotModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NodePropertySnapshotModel>()
            .HasKey(nameof(NodePropertySnapshotModel.Id));

        modelBuilder.Entity<NodePropertySnapshotModel>()
            .Property(x => x.NodeProperties)
            .HasConversion(x => Serialize(x), x => Deserialize<List<NodePropertyEntry>>(x))
            .Metadata
            .SetValueComparer(GetEnumerableComparer<NodePropertyEntry>());
    }

    private static void SqlServer_BuildNodeProfileModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NodeProfileModel>()
            .HasKey(nameof(NodeProfileModel.Id));
    }

    private static void SqlServer_BuildNodeInfoModel(ModelBuilder modelBuilder)
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

    private static void SqlServer_BuildJsonBasedDataModels(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FtpConfigModel>(builder =>
        {
            builder.OwnsOne(
                model => model.Value, ownedNavigationBuilder => { ownedNavigationBuilder.ToJson(); });
        });

        modelBuilder.Entity<NodeEnvVarsConfigModel>(builder =>
        {
            builder.OwnsOne(
                model => model.Value, ownedNavigationBuilder =>
                {
                    ownedNavigationBuilder.ToJson();
                    ownedNavigationBuilder
                        .Property(x => x.EnvironmentVariables)
                        .HasConversion(x => Serialize(x), x => Deserialize<List<StringEntry>>(x))
                        .Metadata
                        .SetValueComparer(GetEnumerableComparer<StringEntry>());
                });
        });

        modelBuilder.Entity<FtpDownloadConfigModel>(builder =>
        {
            builder.OwnsOne(
                model => model.Value, ownedNavigationBuilder => { ownedNavigationBuilder.ToJson(); });
        });

        modelBuilder.Entity<FtpUploadConfigModel>(builder =>
        {
            builder.OwnsOne(
                model => model.Value, ownedNavigationBuilder =>
                {
                    ownedNavigationBuilder.ToJson();

                    ownedNavigationBuilder
                        .Property(x => x.Filters)
                        .HasConversion(x => Serialize(x), x => Deserialize<List<StringEntry>>(x))
                        .Metadata
                        .SetValueComparer(GetEnumerableComparer<StringEntry>());

                    ownedNavigationBuilder.Property(x => x.DateTimeFilters)
                        .HasConversion(x => Serialize(x), x => Deserialize<List<DateTimeFilter>>(x))
                        .Metadata
                        .SetValueComparer(GetEnumerableComparer<DateTimeFilter>());

                    ownedNavigationBuilder.Property(x => x.LengthFilters)
                        .HasConversion(x => Serialize(x), x => Deserialize<List<FileLengthFilter>>(x))
                        .Metadata
                        .SetValueComparer(GetEnumerableComparer<FileLengthFilter>());

                    ownedNavigationBuilder.Property(x => x.DirectoryFilters)
                        .HasConversion(x => Serialize(x), x => Deserialize<List<StringEntry>>(x))
                        .Metadata
                        .SetValueComparer(GetEnumerableComparer<StringEntry>());
                });
        });

        modelBuilder.Entity<KafkaConfigModel>(builder =>
        {
            builder.OwnsOne(
                model => model.Value, ownedNavigationBuilder =>
                {
                    ownedNavigationBuilder.ToJson();
                    ownedNavigationBuilder
                        .Property(x => x.Topics)
                        .HasConversion(x => Serialize(x), x => Deserialize<List<StringEntry>>(x))
                        .Metadata
                        .SetValueComparer(GetEnumerableComparer<StringEntry>());
                });
        });

        modelBuilder.Entity<DatabaseConfigModel>(builder =>
        {
            builder.OwnsOne(
                model => model.Value, ownedNavigationBuilder => { ownedNavigationBuilder.ToJson(); });
        });

        modelBuilder.Entity<TaskDefinitionModel>(builder =>
        {
            builder.OwnsOne(
                model => model.Value, ownedNavigationBuilder =>
                {
                    ownedNavigationBuilder.ToJson();

                    ownedNavigationBuilder.Property(x => x.ChildTaskDefinitions)
                        .HasConversion(x => Serialize(x), x => Deserialize<List<StringEntry>>(x))
                        .Metadata
                        .SetValueComparer(GetEnumerableComparer<StringEntry>());

                    ownedNavigationBuilder.Property(x => x.Options)
                        .HasConversion(x => Serialize(x), x => Deserialize<Dictionary<string, object?>>(x))
                        .Metadata
                        .SetValueComparer(GetEnumerableComparer<KeyValuePair<string, object?>>());


                    ownedNavigationBuilder.Property(x => x.EnvironmentVariables)
                        .HasConversion(x => Serialize(x), x => Deserialize<List<StringEntry>>(x))
                        .Metadata
                        .SetValueComparer(GetEnumerableComparer<StringEntry>());

                    ownedNavigationBuilder.Property(x => x.CronExpressions)
                        .HasConversion(x => Serialize(x), x => Deserialize<List<StringEntry>>(x))
                        .Metadata
                        .SetValueComparer(GetEnumerableComparer<StringEntry>());

                    ownedNavigationBuilder.Property(x => x.NodeList)
                        .HasConversion(x => Serialize(x), x => Deserialize<List<StringEntry>>(x))
                        .Metadata
                        .SetValueComparer(GetEnumerableComparer<StringEntry>());
                });
        });

        modelBuilder.Entity<TaskTypeDescConfigModel>(builder =>
        {
            builder.OwnsOne(
                model => model.Value, ownedNavigationBuilder =>
                {
                    ownedNavigationBuilder.ToJson();
                    ownedNavigationBuilder.Property(x => x.OptionValueEditors)
                        .HasConversion(x => Serialize(x), x => Deserialize<List<StringEntry>>(x))
                        .Metadata
                        .SetValueComparer(GetEnumerableComparer<StringEntry>());
                });
        });

        modelBuilder.Entity<RestApiConfigModel>(builder =>
        {
            builder.OwnsOne(
                model => model.Value, ownedNavigationBuilder => { ownedNavigationBuilder.ToJson(); });
        });

        modelBuilder.Entity<PackageConfigModel>(builder =>
        {
            builder.OwnsOne(
                model => model.Value, ownedNavigationBuilder => { ownedNavigationBuilder.ToJson(); });
        });

        modelBuilder.Entity<WindowsTaskConfigModel>(builder =>
        {
            builder.OwnsOne(
                model => model.Value, ownedNavigationBuilder => { ownedNavigationBuilder.ToJson(); });
        });

        modelBuilder.Entity<NotificationConfigModel>(builder =>
        {
            builder.OwnsOne(
                model => model.Value, ownedNavigationBuilder =>
                {
                    ownedNavigationBuilder.ToJson();
                    ownedNavigationBuilder.Property(x => x.Options)
                        .HasConversion(x => Serialize(x),
                            x => Deserialize<Dictionary<NotificationConfigurationType, object>>(x))
                        .Metadata
                        .SetValueComparer(GetEnumerableComparer<KeyValuePair<NotificationConfigurationType, object>>());
                });
        });

        modelBuilder.Entity<DataQualityStatisticsDefinitionModel>(builder =>
        {
            builder.OwnsOne(
                model => model.Value, ownedNavigationBuilder =>
                {
                    ownedNavigationBuilder.ToJson();
                    ownedNavigationBuilder.Property(x => x.NodeList)
                        .HasConversion(x => Serialize(x), x => Deserialize<List<StringEntry>>(x))
                        .Metadata
                        .SetValueComparer(GetEnumerableComparer<StringEntry>());
                });
        });

        modelBuilder.Entity<FileSystemWatchConfigModel>(builder =>
        {
            builder.OwnsOne(
                model => model.Value, ownedNavigationBuilder =>
                {
                    ownedNavigationBuilder.ToJson();

                    ownedNavigationBuilder.Property(x => x.Filters)
                        .HasConversion(x => Serialize(x), x => Deserialize<List<StringEntry>>(x))
                        .Metadata
                        .SetValueComparer(GetEnumerableComparer<StringEntry>());

                    ownedNavigationBuilder.Property(x => x.NodeList)
                        .HasConversion(x => Serialize(x), x => Deserialize<List<StringEntry>>(x))
                        .Metadata
                        .SetValueComparer(GetEnumerableComparer<StringEntry>());

                    ownedNavigationBuilder.Property(x => x.Feedbacks)
                        .HasConversion(x => Serialize(x), x => Deserialize<List<StringEntry>>(x))
                        .Metadata
                        .SetValueComparer(GetEnumerableComparer<StringEntry>());
                });
        });

        modelBuilder.Entity<TaskLogModel>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.OwnsOne(
                model => model.Value, ownedNavigationBuilder =>
                {
                    ownedNavigationBuilder.ToJson();
                    ownedNavigationBuilder.Property(x => x.LogEntries)
                        .HasConversion(x => Serialize(x), x => Deserialize<List<LogEntry>>(x))
                        .Metadata
                        .SetValueComparer(GetEnumerableComparer<LogEntry>());
                });
        });

        modelBuilder.Entity<ConfigurationVersionRecordModel>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.OwnsOne(
                model => model.Value, ownedNavigationBuilder =>
                {
                    ownedNavigationBuilder.ToJson();
                });
        });

        modelBuilder.Entity<TaskFlowTemplateModel>(builder =>
        {
            builder.HasKey(x => x.Id);
            builder.OwnsOne(
                model => model.Value, ownedNavigationBuilder =>
                {
                    ownedNavigationBuilder.ToJson();
                    ownedNavigationBuilder.Property(x => x.TaskStages)
                        .HasConversion(x => Serialize(x), x => Deserialize<List<TaskFlowTemplateStage>>(x))
                        .Metadata
                        .SetValueComparer(GetEnumerableComparer<TaskFlowTemplateStage>());
                });
        });
    }
}