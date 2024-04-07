using Microsoft.EntityFrameworkCore;
using NodeService.Infrastructure;
using NodeService.Infrastructure.DataModels;
using NodeService.WebServer.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NodeService.WebServerTools.Services
{
    public class ImportFromDbService : BackgroundService
    {
        private readonly ApiService _apiService;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;

        public ImportFromDbService(
            ApiService apiService,
            IDbContextFactory<ApplicationDbContext> dbContextFactory)
        {
            _apiService = apiService;
            _dbContextFactory = dbContextFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            foreach (var item in Directory.GetDirectories("D:\\configs"))
            {
                var fullName = Path.GetFileName(item);
                var type = typeof(ModelBase).Assembly.GetType(fullName);
                foreach (var jsonFile in Directory.GetFiles(item))
                {
                    var obj = JsonSerializer.Deserialize(File.ReadAllText(jsonFile), type);
                    if (type == typeof(FtpConfigModel))
                    {
                       // await dbContext.FtpConfigurationDbSet.AddAsync(obj as FtpConfigModel);
                    }
                    else if (type == typeof(FtpUploadConfigModel))
                    {
                        //await dbContext.FtpUploadConfigurationDbSet.AddAsync(obj as FtpUploadConfigModel);
                    }
                    else if (type == typeof(FtpDownloadConfigModel))
                    {
                        //await dbContext.FtpDownloadConfigurationDbSet.AddAsync(obj as FtpDownloadConfigModel);
                    }
                    else if (type == typeof(MysqlConfigModel))
                    {
                        //await dbContext.MysqlConfigurationDbSet.AddAsync(obj as MysqlConfigModel);
                    }
                    else if (type == typeof(KafkaConfigModel))
                    {
                        //await dbContext.KafkaConfigurationDbSet.AddAsync(obj as KafkaConfigModel);
                    }
                    else if (type == typeof(JobTypeDescConfigModel))
                    {
                        await dbContext.JobTypeDescConfigurationDbSet.AddAsync(obj as JobTypeDescConfigModel);
                    }
                    else if (type == typeof(JobScheduleConfigModel))
                    {
                        continue;
                        var model = obj as JobScheduleConfigModel;
                        foreach (var binding in model.NodeBindings)
                        {
                            model.NodeList.Add(new StringEntry()
                            {
                                Value = binding.OwnerId,
                            });
                        }

                        await dbContext.JobScheduleConfigurationDbSet.AddAsync(model);
                    }
                    else if (type == typeof(PackageConfigModel))
                    {
                        //using var stream = new MemoryStream();
                        //await this._apiService.DownloadPackageAsync(obj as PackageConfigModel, stream);
                        //stream.Position = 0;
                        //await this._apiService.AddOrUpdateAsync(obj as PackageConfigModel, stream);
                    }
                    else if (type == typeof(RestApiConfigModel))
                    {
                        //await dbContext.RestApiConfigurationDbSet.AddAsync(obj as RestApiConfigModel);
                    }
                }
            }
            await dbContext.SaveChangesAsync();
        }
    }
}
