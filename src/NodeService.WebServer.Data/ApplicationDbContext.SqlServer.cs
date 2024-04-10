using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data
{
    public partial class ApplicationDbContext
    {
        private void SqlServer_BuildModels(ModelBuilder modelBuilder)
        {
            SqlServer_BuildClientUpdateInfoModel(modelBuilder);
            SqlServer_BuildConfigurationModels(modelBuilder);
            SqlServer_BuildJobExecutionInstanceModel(modelBuilder);
            SqlServer_BuildNodeInfoModel(modelBuilder);
            SqlServer_BuildNodeProfileModel(modelBuilder);
            SqlServer_BuildNodePropertySnapshotModel(modelBuilder);
            SqlServer_BuildFileUploadRecordModel(modelBuilder);
            SqlServer_BuildJobFireConfigurationModel(modelBuilder);
        }

        private void SqlServer_BuildJobFireConfigurationModel(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<JobFireConfigurationModel>(entityBuilder =>
            {
                entityBuilder.HasKey(nameof(JobFireConfigurationModel.Id));
            });
        }

        private void SqlServer_BuildFileUploadRecordModel(ModelBuilder modelBuilder)
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
                .AutoInclude(true);

            modelBuilder.Entity<ClientUpdateConfigModel>()
                .Property(x => x.Counters)
                .HasConversion(x => Serialize(x), x => Deserialize<List<ClientUpdateCounterModel>>(x))
                .Metadata
                .SetValueComparer(GetEnumerableComparer<ClientUpdateCounterModel>());
        }



        private static void SqlServer_BuildJobExecutionInstanceModel(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<JobExecutionInstanceModel>()
                .HasKey(nameof(JobExecutionInstanceModel.Id));

            modelBuilder.Entity<JobExecutionInstanceModel>()
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

            //modelBuilder.Entity<NodeInfoModel>()
            //    .HasMany(x => x.ConfigurationBindings)
            //    .WithOne(x => x.Owner)
            //    .HasForeignKey(x => x.OwnerId)
            //    .IsRequired();

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
                .AutoInclude(true);

            //modelBuilder.Entity<NodeInfoModel>()
            // .HasMany(e => e.Configurations)
            // .WithMany(e => e.NodeList)
            // .UsingEntity<NodeInfoConfigurationBindingModel>(
            //     l => l.HasOne<ConfigurationModel>(e => e.Target).WithMany(e => e.NodeBindings).HasForeignKey(e => e.TargetId),
            //     r => r.HasOne<NodeInfoModel>(e => e.Owner).WithMany(e => e.ConfigurationBindings).HasForeignKey(e => e.OwnerId));

            //modelBuilder.Entity<NodeInfoModel>()
            //    .HasMany(e => e.Configurations)
            //    .WithMany(e => e.NodeList)
            //    .UsingEntity<NodeInfoConfigurationBindingModel>(x => x.Property(e => e.PublicationDate).HasDefaultValue(DateTime.UtcNow));

        }

        private static void SqlServer_BuildConfigurationModels(ModelBuilder modelBuilder)
        {





            modelBuilder.Entity<FtpConfigModel>(builder =>
            {
                builder.OwnsOne(
                    model => model.Value, ownedNavigationBuilder =>
                    {
                        ownedNavigationBuilder.ToJson();
                    });
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
                    model => model.Value, ownedNavigationBuilder =>
                    {
                        ownedNavigationBuilder.ToJson();
                    });
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

            modelBuilder.Entity<MysqlConfigModel>(builder =>
            {
                builder.OwnsOne(
                    model => model.Value, ownedNavigationBuilder =>
                    {
                        ownedNavigationBuilder.ToJson();
                    });
            });

            modelBuilder.Entity<LogUploadConfigModel>(builder =>
            {
                builder.OwnsOne(
                    model => model.Value, ownedNavigationBuilder =>
                    {
                        ownedNavigationBuilder.ToJson();
                        ownedNavigationBuilder
                               .Property(x => x.LocalDirectories)
                               .HasConversion(x => Serialize(x), x => Deserialize<List<StringEntry>>(x))
                               .Metadata
                               .SetValueComparer(GetEnumerableComparer<StringEntry>());
                    });
            });

            modelBuilder.Entity<JobScheduleConfigModel>(builder =>
            {

                builder.OwnsOne(
                    model => model.Value, ownedNavigationBuilder =>
                    {
                        ownedNavigationBuilder.ToJson();
                        ownedNavigationBuilder
                               .Property(x => x.DnsFilters)
                               .HasConversion(x => Serialize(x), x => Deserialize<List<StringEntry>>(x))
                               .Metadata
                               .SetValueComparer(GetEnumerableComparer<StringEntry>());

                        ownedNavigationBuilder.Property(x => x.IpAddressFilters)
                               .HasConversion(x => Serialize(x), x => Deserialize<List<StringEntry>>(x))
                               .Metadata
                               .SetValueComparer(GetEnumerableComparer<StringEntry>());

                        ownedNavigationBuilder.Property(x => x.ChildJobs)
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

            modelBuilder.Entity<JobTypeDescConfigModel>(builder =>
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
                    model => model.Value, ownedNavigationBuilder =>
                    {
                        ownedNavigationBuilder.ToJson();
                    });
            });

            modelBuilder.Entity<PackageConfigModel>(builder =>
            {
                builder.OwnsOne(
                    model => model.Value, ownedNavigationBuilder =>
                    {
                        ownedNavigationBuilder.ToJson();
                    });
            });

            modelBuilder.Entity<NotificationConfigModel>(builder =>
            {
                builder.OwnsOne(
                    model => model.Value, ownedNavigationBuilder =>
                    {
                        ownedNavigationBuilder.OwnsOne(x => x.Email, builder =>
                        {
                            builder.Property(x => x.CC)
                                .HasConversion(x => Serialize(x), x => Deserialize<List<StringEntry>>(x))
                                .Metadata
                                .SetValueComparer(GetEnumerableComparer<StringEntry>());
                            builder.Property(x => x.To)
                                .HasConversion(x => Serialize(x), x => Deserialize<List<StringEntry>>(x))
                                .Metadata
                                .SetValueComparer(GetEnumerableComparer<StringEntry>());
                        });
                        ownedNavigationBuilder.OwnsOne(x => x.Lark);
                        ownedNavigationBuilder.ToJson();

                    });
            });
        }
    }
}
