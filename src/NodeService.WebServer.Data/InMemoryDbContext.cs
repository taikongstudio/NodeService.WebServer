﻿using Microsoft.EntityFrameworkCore.ChangeTracking;
using NodeService.Infrastructure.Logging;
using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace NodeService.WebServer.Data
{
    public class InMemoryDbContext : DbContext
    {
        public InMemoryDbContext(DbContextOptions<InMemoryDbContext> options) : base(options)
        {

        }


        protected InMemoryDbContext(DbContextOptions contextOptions)
            : base(contextOptions)
        {
        }

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

            SqlServer_BuildModels(modelBuilder);
        }

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
                                        .Property(x => x.TaskExecutionNodeList)
                                        .HasConversion(x => Serialize(x), x => Deserialize<List<TaskExecutionNodeInfo>>(x))
                                        .Metadata
                                        .SetValueComparer(GetEnumerableComparer<TaskExecutionNodeInfo>());

                        ownedNavigationBuilder
                                        .Property(x => x.NodeList)
                                        .HasConversion(x => Serialize(x), x => Deserialize<List<StringEntry>>(x))
                                        .Metadata
                                        .SetValueComparer(GetEnumerableComparer<StringEntry>());
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

                        ownedNavigationBuilder.Property(x => x.FileGlobbingPatterns)
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
                            .HasConversion(x => Serialize(x), x => Deserialize<List<TaskFlowStageTemplate>>(x))
                            .Metadata
                            .SetValueComparer(GetEnumerableComparer<TaskFlowStageTemplate>());
                    });
            });

            modelBuilder.Entity<TaskFlowExecutionInstanceModel>(builder =>
            {
                builder.HasKey(x => x.Id);
                builder.OwnsOne(
                    model => model.Value, ownedNavigationBuilder =>
                    {
                        ownedNavigationBuilder.ToJson();
                        ownedNavigationBuilder.Property(x => x.TaskStages)
                            .HasConversion(x => Serialize(x), x => Deserialize<List<TaskFlowStageExecutionInstance>>(x))
                            .Metadata
                            .SetValueComparer(GetEnumerableComparer<TaskFlowStageExecutionInstance>());
                    });
            });

            modelBuilder.Entity<NodeFileSyncRecordModel>(builder =>
            {
                builder.HasKey(x => x.Id);
                builder.OwnsOne(
                    model => model.Value, ownedNavigationBuilder =>
                    {
                        ownedNavigationBuilder.ToJson();
                    });
            });

            modelBuilder.Entity<NodeFileSystemInfoModel>(builder =>
            {
                builder.HasKey(x => x.Id);
                builder.OwnsOne(
                    model => model.Value, ownedNavigationBuilder =>
                    {
                        ownedNavigationBuilder.ToJson();
                    });
            });
        }
    }
}
