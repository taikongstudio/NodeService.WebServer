using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;

namespace NodeService.WebServer.Controllers;

public partial class CommonConfigController
{
    [HttpGet("/api/CommonConfig/NodeSetting/")]
    public async Task<ApiResponse<NodeSettings>> QueryNodeSettingsAsync()
    {
        var rsp = new ApiResponse<NodeSettings>();
        try
        {
            NodeSettings? result = null;
            var repoFactory = _serviceProvider.GetService<ApplicationRepositoryFactory<PropertyBag>>();
            using var repo = repoFactory.CreateRepository();
            var propertyBag = await repo.FirstOrDefaultAsync(new PropertyBagSpecification(nameof(NodeSettings)));
            if (propertyBag == null)
            {
                result = new NodeSettings();
                propertyBag = new PropertyBag();
                propertyBag.Add("Id", "NodeSettings");
                propertyBag.Add("Value", JsonSerializer.Serialize(result));
                propertyBag.Add("CreatedDate", DateTime.UtcNow);
                await repo.AddAsync(propertyBag);
            }
            else
            {
                result = JsonSerializer.Deserialize<NodeSettings>(propertyBag["Value"] as string);
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

    [HttpPost("/api/CommonConfig/NodeSetting/update")]
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
        }
        catch (Exception ex)
        {
            rsp.ErrorCode = ex.HResult;
            rsp.Message = ex.ToString();
        }

        return rsp;
    }
}