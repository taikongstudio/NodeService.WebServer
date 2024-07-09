using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;

namespace NodeService.WebServer.Controllers;

public partial class ConfigurationController
{
    [HttpGet("/api/Configuration/NodeSettings/")]
    public async Task<ApiResponse<NodeSettings>> QueryNodeSettingsAsync()
    {
        var rsp = new ApiResponse<NodeSettings>();
        try
        {
            NodeSettings? result = null;
            if (!_memoryCache.TryGetValue<NodeSettings>(nameof(NodeSettings), out result))
            {
                var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<PropertyBag>>();
                using var propertyBagRepo = repoFactory.CreateRepository();
                var propertyBag =
                    await propertyBagRepo.FirstOrDefaultAsync(new PropertyBagSpecification(nameof(NodeSettings)));
                if (propertyBag == null)
                {
                    result = new NodeSettings();
                    propertyBag = new PropertyBag();
                    propertyBag.Add("Id", "NodeSettings");
                    propertyBag.Add("Value", JsonSerializer.Serialize(result));
                    propertyBag.Add("CreatedDate", DateTime.UtcNow);
                    await propertyBagRepo.AddAsync(propertyBag);
                }
                else
                {
                    result = JsonSerializer.Deserialize<NodeSettings>(propertyBag["Value"] as string);
                }

                _memoryCache.Set(nameof(NodeSettings), result);
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

    [HttpPost("/api/Configuration/NodeSettings/Update")]
    public async Task<ApiResponse> UpdateNodeSettingAsync([FromBody] NodeSettings model)
    {
        var rsp = new ApiResponse();
        try
        {
            var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<PropertyBag>>();
            using var repo = repoFactory.CreateRepository();
            var propertyBag = await repo.FirstOrDefaultAsync(new PropertyBagSpecification(nameof(NodeSettings)));

            var value = JsonSerializer.Serialize(model);
            var count = await repo.DbContext.Set<PropertyBag>()
                .Where(x => x["Id"] == nameof(NodeSettings))
                .ExecuteUpdateAsync(setPropertyCalls => setPropertyCalls.SetProperty(x => x["Value"], x => value));
            _memoryCache.Remove(nameof(NodeSettings));
        }
        catch (Exception ex)
        {
            rsp.ErrorCode = ex.HResult;
            rsp.Message = ex.ToString();
        }

        return rsp;
    }
}