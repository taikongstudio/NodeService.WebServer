using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;

namespace NodeService.WebServer.Controllers;

public partial class ConfigurationController
{
    [HttpGet("/api/CommonConfig/NodeSettings/")]
    public async Task<ApiResponse<NodeSettings>> QueryNodeSettingsAsync(CancellationToken cancellationToken = default)
    {
        var rsp = new ApiResponse<NodeSettings>();
        try
        {
            NodeSettings? result = null;
            if (!_memoryCache.TryGetValue<NodeSettings>(nameof(NodeSettings), out result))
            {
                result = await _configurationQueryService.QueryNodeSettingsAsync(cancellationToken);

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


    [HttpPost("/api/CommonConfig/NodeSettings/Update")]
    public async Task<ApiResponse> UpdateNodeSettingAsync([FromBody] NodeSettings model,
        CancellationToken cancellationToken = default)
    {
        var rsp = new ApiResponse();
        try
        {
            await _configurationQueryService.UpdateNodeSettingsAsync(model, cancellationToken);
        }
        catch (Exception ex)
        {
            rsp.ErrorCode = ex.HResult;
            rsp.Message = ex.ToString();
        }

        return rsp;
    }


}