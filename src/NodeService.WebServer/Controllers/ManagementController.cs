﻿namespace NodeService.WebServer.Controllers;

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


    [HttpGet("/api/Test/GenNode")]
    public async Task<ApiResponse<int>> TestAsync()
    {
        var apiResponse = new ApiResponse<int>();
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        for (var i = 0; i < 3000; i++)
        {
            var nodeInfo = NodeInfoModel.Create(Guid.NewGuid().ToString(), i.ToString(), NodeDeviceType.Computer);
            dbContext.NodeInfoDbSet.Add(nodeInfo);
        }

        var changesCount = await dbContext.SaveChangesAsync();
        apiResponse.SetResult(changesCount);
        return apiResponse;
    }

    [HttpGet("/api/Test/Clearjobtypedesc")]
    public async Task<ApiResponse<int>> ClearJobTypeDescAsync()
    {
        var apiResponse = new ApiResponse<int>();
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        await dbContext.TaskDefinitionDbSet.ExecuteDeleteAsync();
        var changesCount = await dbContext.SaveChangesAsync();
        apiResponse.SetResult(changesCount);
        return apiResponse;
    }

    [HttpGet("/api/Test/GenTaskTypedesc")]
    public async Task<ApiResponse<int>> GenJobTypeDescAsync()
    {
        var apiResponse = new ApiResponse<int>();

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        var serviceOptionsTypes = typeof(TaskOptionsBase)
            .Assembly
            .GetTypes()
            .Where(x => !x.IsAbstract && x.IsAssignableTo(typeof(TaskOptionsBase)));

        foreach (var type in serviceOptionsTypes)
        {
            var model = new TaskTypeDescConfigModel();
            model.Id = Guid.NewGuid().ToString();
            model.Name = type.Name.Replace("Options", string.Empty);
            model.FullName = "NodeService.ServiceHost.Tasks." + model.Name;
            model.Description = type.Name;
            dbContext.TaskTypeDescConfigurationDbSet.Add(model);
            foreach (var propertyInfo in type.GetProperties())
                if (propertyInfo.PropertyType == typeof(bool))
                    model.OptionValueEditors.Add(new StringEntry
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = propertyInfo.Name,
                        Value = OptionValueEditors.Types.FirstOrDefault(x => x.Value == OptionValueType.BooleanValue)
                            .Name,
                        Tag = OptionValueType.BooleanValue.ToString()
                    });
                else if (propertyInfo.PropertyType == typeof(double))
                    model.OptionValueEditors.Add(new StringEntry
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = propertyInfo.Name,
                        Value = OptionValueEditors.Types.FirstOrDefault(x => x.Value == OptionValueType.NumberValue)
                            .Name,
                        Tag = OptionValueType.NumberValue.ToString()
                    });
                else if (propertyInfo.PropertyType == typeof(string))
                    model.OptionValueEditors.Add(new StringEntry
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = propertyInfo.Name,
                        Value = OptionValueEditors.Types.FirstOrDefault(x => x.Value == OptionValueType.TextValue).Name,
                        Tag = OptionValueType.TextValue.ToString()
                    });
                else if (propertyInfo.PropertyType.IsAssignableTo(typeof(IEnumerable<string>)))
                    model.OptionValueEditors.Add(new StringEntry
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = propertyInfo.Name,
                        Value = OptionValueEditors.Types.FirstOrDefault(x => x.Value == OptionValueType.TextArrayValue)
                            .Name,
                        Tag = OptionValueType.TextArrayValue.ToString()
                    });
                else if (propertyInfo.PropertyType.BaseType == typeof(KafkaConfigModel))
                    model.OptionValueEditors.Add(new StringEntry
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = propertyInfo.Name,
                        Value = OptionValueEditors.Types
                            .FirstOrDefault(x => x.Value == OptionValueType.KafkaConfigurationValue).Name,
                        Tag = OptionValueType.KafkaConfigurationValue.ToString()
                    });
                else if (propertyInfo.PropertyType.IsAssignableTo(typeof(IEnumerable<KafkaConfigModel>)))
                    model.OptionValueEditors.Add(new StringEntry
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = propertyInfo.Name,
                        Value = OptionValueEditors.Types
                            .FirstOrDefault(x => x.Value == OptionValueType.KafkaConfigurationListValue).Name,
                        Tag = OptionValueType.KafkaConfigurationListValue.ToString()
                    });
                else if (propertyInfo.PropertyType.BaseType == typeof(PackageConfigModel))
                    model.OptionValueEditors.Add(new StringEntry
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = propertyInfo.Name,
                        Value = OptionValueEditors.Types
                            .FirstOrDefault(x => x.Value == OptionValueType.PackageConfigurationValue).Name,
                        Tag = OptionValueType.PackageConfigurationValue.ToString()
                    });
                else if (propertyInfo.PropertyType.IsAssignableTo(typeof(IEnumerable<PackageConfigModel>)))
                    model.OptionValueEditors.Add(new StringEntry
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = propertyInfo.Name,
                        Value = OptionValueEditors.Types
                            .FirstOrDefault(x => x.Value == OptionValueType.PackageConfigurationListValue).Name,
                        Tag = OptionValueType.PackageConfigurationListValue.ToString()
                    });
                else if (propertyInfo.PropertyType.BaseType == typeof(FtpConfigModel))
                    model.OptionValueEditors.Add(new StringEntry
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = propertyInfo.Name,
                        Value = OptionValueEditors.Types
                            .FirstOrDefault(x => x.Value == OptionValueType.FtpConfigurationValue).Name,
                        Tag = OptionValueType.FtpConfigurationValue.ToString()
                    });
                else if (propertyInfo.PropertyType.IsAssignableTo(typeof(IEnumerable<FtpConfigModel>)))
                    model.OptionValueEditors.Add(new StringEntry
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = propertyInfo.Name,
                        Value = OptionValueEditors.Types
                            .FirstOrDefault(x => x.Value == OptionValueType.FtpConfigurationListValue).Name,
                        Tag = OptionValueType.FtpConfigurationListValue.ToString()
                    });
                else if (propertyInfo.PropertyType.BaseType == typeof(DatabaseConfigModel))
                    model.OptionValueEditors.Add(new StringEntry
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = propertyInfo.Name,
                        Value = OptionValueEditors.Types
                            .FirstOrDefault(x => x.Value == OptionValueType.MysqlConfigurationValue).Name,
                        Tag = OptionValueType.MysqlConfigurationValue.ToString()
                    });
                else if (propertyInfo.PropertyType.IsAssignableTo(typeof(IEnumerable<DatabaseConfigModel>)))
                    model.OptionValueEditors.Add(new StringEntry
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = propertyInfo.Name,
                        Value = OptionValueEditors.Types
                            .FirstOrDefault(x => x.Value == OptionValueType.MysqlConfigurationListValue).Name,
                        Tag = OptionValueType.MysqlConfigurationListValue.ToString()
                    });
                else if (propertyInfo.PropertyType.BaseType == typeof(FtpUploadConfigModel))
                    model.OptionValueEditors.Add(new StringEntry
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = propertyInfo.Name,
                        Value = OptionValueEditors.Types
                            .FirstOrDefault(x => x.Value == OptionValueType.FtpUploadConfigurationValue).Name,
                        Tag = OptionValueType.FtpUploadConfigurationValue.ToString()
                    });
                else if (propertyInfo.PropertyType.IsAssignableTo(typeof(IEnumerable<FtpUploadConfigModel>)))
                    model.OptionValueEditors.Add(new StringEntry
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = propertyInfo.Name,
                        Value = OptionValueEditors.Types
                            .FirstOrDefault(x => x.Value == OptionValueType.FtpUploadConfigurationListValue).Name,
                        Tag = OptionValueType.FtpUploadConfigurationListValue.ToString()
                    });
                else if (propertyInfo.PropertyType.BaseType == typeof(NodeEnvVarsConfigModel))
                    model.OptionValueEditors.Add(new StringEntry
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = propertyInfo.Name,
                        Value = OptionValueEditors.Types
                            .FirstOrDefault(x => x.Value == OptionValueType.NodeEnvVarsConfigurationValue).Name,
                        Tag = OptionValueType.NodeEnvVarsConfigurationValue.ToString()
                    });
                else if (propertyInfo.PropertyType.IsAssignableTo(typeof(IEnumerable<NodeEnvVarsConfigModel>)))
                    model.OptionValueEditors.Add(new StringEntry
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = propertyInfo.Name,
                        Value = OptionValueEditors.Types
                            .FirstOrDefault(x => x.Value == OptionValueType.NodeEnvVarsConfigurationListValue).Name,
                        Tag = OptionValueType.NodeEnvVarsConfigurationListValue.ToString()
                    });
                else if (propertyInfo.PropertyType.BaseType == typeof(RestApiConfigModel))
                    model.OptionValueEditors.Add(new StringEntry
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = propertyInfo.Name,
                        Value = OptionValueEditors.Types
                            .FirstOrDefault(x => x.Value == OptionValueType.RestApiConfigurationValue).Name,
                        Tag = OptionValueType.RestApiConfigurationValue.ToString()
                    });
                else if (propertyInfo.PropertyType.IsAssignableTo(typeof(IEnumerable<RestApiConfigModel>)))
                    model.OptionValueEditors.Add(new StringEntry
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = propertyInfo.Name,
                        Value = OptionValueEditors.Types
                            .FirstOrDefault(x => x.Value == OptionValueType.RestApiConfigurationListValue).Name,
                        Tag = OptionValueType.RestApiConfigurationListValue.ToString()
                    });
        }


        var changesCount = await dbContext.SaveChangesAsync();
        apiResponse.SetResult(changesCount);
        return apiResponse;
    }
}