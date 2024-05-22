using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Data.Repositories;

namespace NodeService.WebServer.Controllers
{
    public partial class DataQualityController
    {
        [HttpGet("/api/DataQuality/Settings/")]
        public async Task<ApiResponse<DataQualitySettings>> QueryDataQualitySettingsAsync()
        {
            var rsp = new ApiResponse<DataQualitySettings>();
            try
            {
                DataQualitySettings? result = null;
                if (!_memoryCache.TryGetValue<DataQualitySettings>(nameof(DataQualitySettings), out result))
                {
                    var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<PropertyBag>>();
                    using var propertyBagRepo = repoFactory.CreateRepository();
                    var propertyBag = await propertyBagRepo.FirstOrDefaultAsync(new PropertyBagSpecification(nameof(DataQualitySettings)));
                    if (propertyBag == null)
                    {
                        result = new DataQualitySettings()
                        {
                            StatisticsLimitDate = new DateTime(2022, 1, 1)
                        };
                        propertyBag = new PropertyBag();
                        propertyBag.Add("Id", nameof(DataQualitySettings));
                        propertyBag.Add("Value", JsonSerializer.Serialize(result));
                        propertyBag.Add("CreatedDate", DateTime.UtcNow);
                        await propertyBagRepo.AddAsync(propertyBag);
                    }
                    else
                    {
                        result = JsonSerializer.Deserialize<DataQualitySettings>(propertyBag["Value"] as string);
                    }
                    _memoryCache.Set(nameof(DataQualitySettings), result);
                }

                rsp.SetResult(result);
            }
            catch (Exception ex)
            {
                rsp.ErrorCode = ex.HResult;
                rsp.Message = ex.ToString();
            }

            return rsp;
        }

        [HttpPost("/api/DataQuality/Settings/Update")]
        public async Task<ApiResponse> UpdateDataQualitySettingAsync([FromBody] DataQualitySettings model)
        {
            var rsp = new ApiResponse();
            try
            {
                var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<PropertyBag>>();
                using var propertyBagRepo = repoFactory.CreateRepository();
                var propertyBag = await propertyBagRepo.FirstOrDefaultAsync(new PropertyBagSpecification(nameof(DataQualitySettings)));

                var value = JsonSerializer.Serialize(model);
                var count = await propertyBagRepo.DbContext.Set<PropertyBag>()
                    .Where(x => x["Id"] == nameof(DataQualitySettings))
                    .ExecuteUpdateAsync(setPropertyCalls => setPropertyCalls.SetProperty(x => x["Value"], x => value));
                _memoryCache.Remove(nameof(DataQualitySettings));
            }
            catch (Exception ex)
            {
                rsp.ErrorCode = ex.HResult;
                rsp.Message = ex.ToString();
            }

            return rsp;
        }
    }
}
