using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Controllers;

public partial class ClientUpdateController
{
    [HttpPost("/api/ClientUpdate/Counters/AddOrUpdate")]
    public async Task<ApiResponse<bool>>
        AddOrUpdateCountersAsync([FromBody] ClientUpdateLog log, CancellationToken cancellationToken = default)
    {
        var rsp = new ApiResponse<bool>();
        try
        {
            ArgumentNullException.ThrowIfNull(log);
            await _clientUpdateLogProduceQueue.SendAsync(log, cancellationToken);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
            rsp.ErrorCode = ex.HResult;
            rsp.Message = ex.Message;
        }

        return rsp;
    }
}