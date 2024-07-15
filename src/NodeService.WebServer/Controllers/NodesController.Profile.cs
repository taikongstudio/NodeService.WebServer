namespace NodeService.WebServer.Controllers;

public partial class NodesController
{
    [HttpPost("/api/Nodes/~/{nodeId}/Profile/Update")]
    public async Task<ApiResponse> UpdateNodeInfoAsync(
        string nodeId,
        [FromBody] UpdateNodeProfileModel value,
        CancellationToken cancellationToken = default)
    {
        var apiResponse = new ApiResponse();
        try
        {
            ArgumentNullException.ThrowIfNull(value, nameof(value));
            await _nodeInfoQueryService.UpdateNodeProfileAsync(nodeId, value);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }

        return apiResponse;
    }
}