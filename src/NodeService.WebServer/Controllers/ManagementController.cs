namespace NodeService.WebServer.Controllers
{
    [ApiController]
    [Route("api/[controller]/[action]")]
    public class ManagementController : Controller
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly IServiceProvider _serviceProvider;
        public ManagementController(
            IServiceProvider serviceProvider,
            IDbContextFactory<ApplicationDbContext> dbContextFactory
            )
        {
            _serviceProvider = serviceProvider;
            _dbContextFactory = dbContextFactory;
        }

        [HttpGet("/api/management/sync")]
        public async Task<ApiResponse<int>> SyncNodeInfoFromMachineInfoAsync()
        {
            ApiResponse<int> apiResponse = new ApiResponse<int>();
            using var scope = _serviceProvider.CreateScope();
            using var profileDbContext = _serviceProvider.GetService<ApplicationProfileDbContext>();
            var machineInfoList = profileDbContext.MachineInfoDbSet.ToArray();
            using var dbContext = _dbContextFactory.CreateDbContext();
            var nodesList = dbContext.NodeInfoDbSet.Include(x => x.Profile).AsSplitQuery().ToList();

            foreach (var machineInfo in machineInfoList)
            {
                string computerName = machineInfo.computer_name;
                var nodeInfo = nodesList.FirstOrDefault(
                    x =>
                    string.Equals(x.Name, computerName, StringComparison.OrdinalIgnoreCase)
                    );
                if (nodeInfo == null)
                {
                    continue;
                }
                if (nodeInfo.Profile.IpAddress == null)
                {
                    continue;
                }
                if (nodeInfo.Profile.IpAddress != nodeInfo.Profile.IpAddress || !nodeInfo.Profile.IpAddress.Contains(nodeInfo.Profile.IpAddress))
                {
                    continue;
                }
                nodeInfo.Profile.TestInfo = machineInfo.test_info;
                nodeInfo.Profile.Remarks = machineInfo.remarks;
                nodeInfo.Profile.LabName = machineInfo.lab_name;
                nodeInfo.Profile.Usages = machineInfo.usages;
                nodeInfo.Profile.LabArea = machineInfo.lab_area;
            }

            var changesCount = await dbContext.SaveChangesAsync();
            apiResponse.Result = changesCount;
            return apiResponse;
        }

        [HttpGet("/api/management/gennode")]
        public async Task<ApiResponse<int>> TestAsync()
        {
            ApiResponse<int> apiResponse = new ApiResponse<int>();
            using var dbContext = _dbContextFactory.CreateDbContext();
            for (int i = 0; i < 1000; i++)
            {
                var nodeInfo = NodeInfoModel.Create(Guid.NewGuid().ToString(), i.ToString());
                dbContext.NodeInfoDbSet.Add(nodeInfo);
            }

            var changesCount = await dbContext.SaveChangesAsync();
            apiResponse.Result = changesCount;
            return apiResponse;
        }

        [HttpGet("/api/management/clearjobtypedesc")]
        public async Task<ApiResponse<int>> ClearJobTypeDescAsync()
        {
            ApiResponse<int> apiResponse = new ApiResponse<int>();
            using var dbContext = _dbContextFactory.CreateDbContext();
            dbContext.JobScheduleConfigurationDbSet.RemoveRange(dbContext.JobScheduleConfigurationDbSet.ToArray());
            apiResponse.Result = await dbContext.SaveChangesAsync();
            return apiResponse;
        }

        [HttpGet("/api/management/genjobtypedesc")]
        public async Task<ApiResponse<int>> GenJobTypeDescAsync()
        {
            ApiResponse<int> apiResponse = new ApiResponse<int>();

            using var dbContext = _dbContextFactory.CreateDbContext();

            var serviceOptionsTypes = typeof(JobOptionsBase)
                .Assembly
                .GetTypes()
                .Where(x => !x.IsAbstract && x.IsAssignableTo(typeof(JobOptionsBase)));

            foreach (var type in serviceOptionsTypes)
            {
                JobTypeDescConfigModel model = new JobTypeDescConfigModel();
                model.Id = Guid.NewGuid().ToString();
                model.Name = type.Name.Replace("Options", string.Empty);
                model.FullName = "NodeService.WindowsService.Services." + model.Name;
                model.Description = type.Name;
                dbContext.JobTypeDescConfigurationDbSet.Add(model);
                foreach (var propertyInfo in type.GetProperties())
                {
                    if (propertyInfo.PropertyType == typeof(bool))
                    {
                        model.OptionValueEditors.Add(new StringEntry()
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = propertyInfo.Name,
                            Value = OptionValueEditors.Types.FirstOrDefault(x => x.Value == OptionValueType.BooleanValue).Name,
                            Tag = OptionValueType.BooleanValue.ToString()
                        });
                    }
                    else if (propertyInfo.PropertyType == typeof(double))
                    {
                        model.OptionValueEditors.Add(new StringEntry()
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = propertyInfo.Name,
                            Value = OptionValueEditors.Types.FirstOrDefault(x => x.Value == OptionValueType.NumberValue).Name,
                            Tag = OptionValueType.NumberValue.ToString()
                        });
                    }
                    else if (propertyInfo.PropertyType == typeof(string))
                    {
                        model.OptionValueEditors.Add(new StringEntry()
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = propertyInfo.Name,
                            Value = OptionValueEditors.Types.FirstOrDefault(x => x.Value == OptionValueType.TextValue).Name,
                            Tag = OptionValueType.TextValue.ToString()
                        });
                    }
                    else if (propertyInfo.PropertyType.IsAssignableTo(typeof(IEnumerable<string>)))
                    {
                        model.OptionValueEditors.Add(new StringEntry()
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = propertyInfo.Name,
                            Value = OptionValueEditors.Types.FirstOrDefault(x => x.Value == OptionValueType.TextArrayValue).Name,
                            Tag = OptionValueType.TextArrayValue.ToString()
                        });
                    }
                    else if (propertyInfo.PropertyType.BaseType == typeof(KafkaConfigModel))
                    {
                        model.OptionValueEditors.Add(new StringEntry()
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = propertyInfo.Name,
                            Value = OptionValueEditors.Types.FirstOrDefault(x => x.Value == OptionValueType.KafkaConfigurationValue).Name,
                            Tag = OptionValueType.KafkaConfigurationValue.ToString()
                        });
                    }
                    else if (propertyInfo.PropertyType.IsAssignableTo(typeof(IEnumerable<KafkaConfigModel>)))
                    {
                        model.OptionValueEditors.Add(new StringEntry()
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = propertyInfo.Name,
                            Value = OptionValueEditors.Types.FirstOrDefault(x => x.Value == OptionValueType.KafkaConfigurationListValue).Name,
                            Tag = OptionValueType.KafkaConfigurationListValue.ToString()
                        });
                    }
                    else if (propertyInfo.PropertyType.BaseType == typeof(PackageConfigModel))
                    {
                        model.OptionValueEditors.Add(new StringEntry()
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = propertyInfo.Name,
                            Value = OptionValueEditors.Types.FirstOrDefault(x => x.Value == OptionValueType.PackageConfigurationValue).Name,
                            Tag = OptionValueType.PackageConfigurationValue.ToString()
                        });
                    }
                    else if (propertyInfo.PropertyType.IsAssignableTo(typeof(IEnumerable<PackageConfigModel>)))
                    {
                        model.OptionValueEditors.Add(new StringEntry()
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = propertyInfo.Name,
                            Value = OptionValueEditors.Types.FirstOrDefault(x => x.Value == OptionValueType.PackageConfigurationListValue).Name,
                            Tag = OptionValueType.PackageConfigurationListValue.ToString()
                        });
                    }
                    else if (propertyInfo.PropertyType.BaseType == typeof(FtpConfigModel))
                    {
                        model.OptionValueEditors.Add(new StringEntry()
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = propertyInfo.Name,
                            Value = OptionValueEditors.Types.FirstOrDefault(x => x.Value == OptionValueType.FtpConfigurationValue).Name,
                            Tag = OptionValueType.FtpConfigurationValue.ToString()
                        });
                    }
                    else if (propertyInfo.PropertyType.IsAssignableTo(typeof(IEnumerable<FtpConfigModel>)))
                    {
                        model.OptionValueEditors.Add(new StringEntry()
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = propertyInfo.Name,
                            Value = OptionValueEditors.Types.FirstOrDefault(x => x.Value == OptionValueType.FtpConfigurationListValue).Name,
                            Tag = OptionValueType.FtpConfigurationListValue.ToString()
                        });
                    }
                    else if (propertyInfo.PropertyType.BaseType == typeof(MysqlConfigModel))
                    {
                        model.OptionValueEditors.Add(new StringEntry()
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = propertyInfo.Name,
                            Value = OptionValueEditors.Types.FirstOrDefault(x => x.Value == OptionValueType.MysqlConfigurationValue).Name,
                            Tag = OptionValueType.MysqlConfigurationValue.ToString()
                        });
                    }
                    else if (propertyInfo.PropertyType.IsAssignableTo(typeof(IEnumerable<MysqlConfigModel>)))
                    {
                        model.OptionValueEditors.Add(new StringEntry()
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = propertyInfo.Name,
                            Value = OptionValueEditors.Types.FirstOrDefault(x => x.Value == OptionValueType.MysqlConfigurationListValue).Name,
                            Tag = OptionValueType.MysqlConfigurationListValue.ToString()
                        });
                    }
                    else if (propertyInfo.PropertyType.BaseType == typeof(LogUploadConfigModel))
                    {
                        model.OptionValueEditors.Add(new StringEntry()
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = propertyInfo.Name,
                            Value = OptionValueEditors.Types.FirstOrDefault(x => x.Value == OptionValueType.LogUploadConfigurationValue).Name,
                            Tag = OptionValueType.LogUploadConfigurationValue.ToString()
                        });
                    }
                    else if (propertyInfo.PropertyType.IsAssignableTo(typeof(IEnumerable<LogUploadConfigModel>)))
                    {
                        model.OptionValueEditors.Add(new StringEntry()
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = propertyInfo.Name,
                            Value = OptionValueEditors.Types.FirstOrDefault(x => x.Value == OptionValueType.LogUploadConfigurationListValue).Name,
                            Tag = OptionValueType.LogUploadConfigurationListValue.ToString()
                        });
                    }
                    else if (propertyInfo.PropertyType.BaseType == typeof(FtpUploadConfigModel))
                    {
                        model.OptionValueEditors.Add(new StringEntry()
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = propertyInfo.Name,
                            Value = OptionValueEditors.Types.FirstOrDefault(x => x.Value == OptionValueType.FtpUploadConfigurationValue).Name,
                            Tag = OptionValueType.FtpUploadConfigurationValue.ToString()
                        });
                    }
                    else if (propertyInfo.PropertyType.IsAssignableTo(typeof(IEnumerable<FtpUploadConfigModel>)))
                    {
                        model.OptionValueEditors.Add(new StringEntry()
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = propertyInfo.Name,
                            Value = OptionValueEditors.Types.FirstOrDefault(x => x.Value == OptionValueType.FtpUploadConfigurationListValue).Name,
                            Tag = OptionValueType.FtpUploadConfigurationListValue.ToString()
                        });
                    }
                    else if (propertyInfo.PropertyType.BaseType == typeof(NodeEnvVarsConfigModel))
                    {
                        model.OptionValueEditors.Add(new StringEntry()
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = propertyInfo.Name,
                            Value = OptionValueEditors.Types.FirstOrDefault(x => x.Value == OptionValueType.NodeEnvVarsConfigurationValue).Name,
                            Tag = OptionValueType.NodeEnvVarsConfigurationValue.ToString()
                        });
                    }
                    else if (propertyInfo.PropertyType.IsAssignableTo(typeof(IEnumerable<NodeEnvVarsConfigModel>)))
                    {
                        model.OptionValueEditors.Add(new StringEntry()
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = propertyInfo.Name,
                            Value = OptionValueEditors.Types.FirstOrDefault(x => x.Value == OptionValueType.NodeEnvVarsConfigurationListValue).Name,
                            Tag = OptionValueType.NodeEnvVarsConfigurationListValue.ToString()
                        });
                    }
                    else if (propertyInfo.PropertyType.BaseType == typeof(RestApiConfigModel))
                    {
                        model.OptionValueEditors.Add(new StringEntry()
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = propertyInfo.Name,
                            Value = OptionValueEditors.Types.FirstOrDefault(x => x.Value == OptionValueType.RestApiConfigurationValue).Name,
                            Tag = OptionValueType.RestApiConfigurationValue.ToString()
                        });
                    }
                    else if (propertyInfo.PropertyType.IsAssignableTo(typeof(IEnumerable<RestApiConfigModel>)))
                    {
                        model.OptionValueEditors.Add(new StringEntry()
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = propertyInfo.Name,
                            Value = OptionValueEditors.Types.FirstOrDefault(x => x.Value == OptionValueType.RestApiConfigurationListValue).Name,
                            Tag = OptionValueType.RestApiConfigurationListValue.ToString()
                        });
                    }
                }
            }


            var changesCount = await dbContext.SaveChangesAsync();
            apiResponse.Result = changesCount;
            return apiResponse;
        }

    }
}
