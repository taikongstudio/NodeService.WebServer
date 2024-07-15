namespace NodeService.WebServer.Controllers;

public partial class NodesController
{
    [HttpGet("/api/Nodes/~/{nodeId}/props")]
    public async Task<ApiResponse<NodePropertySnapshotModel?>> QueryNodePropsAsync(
        string nodeId,
        CancellationToken cancellationToken = default)
    {
        var rsp = new ApiResponse<NodePropertySnapshotModel>();
        try
        {
            var nodeProps = await _nodeInfoQueryService.QueryNodePropsAsync(nodeId, cancellationToken);
            rsp.SetResult(nodeProps);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
            rsp.ErrorCode = ex.HResult;
            rsp.Message = ex.ToString();
        }

        return rsp;
    }
}