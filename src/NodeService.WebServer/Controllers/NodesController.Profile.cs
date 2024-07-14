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
            await using var nodeInfoRepo = await _nodeInfoRepoFactory.CreateRepositoryAsync(cancellationToken);
            var nodeInfo = await nodeInfoRepo.GetByIdAsync(nodeId, cancellationToken);
            if (nodeInfo == null)
            {
                apiResponse.ErrorCode = -1;
                apiResponse.Message = "invalid node id";
            }
            else
            {
                nodeInfo.Profile.TestInfo = value.TestInfo;
                nodeInfo.Profile.LabArea = value.LabArea;
                nodeInfo.Profile.LabName = value.LabName;
                nodeInfo.Profile.Usages = value.Usages;
                nodeInfo.Profile.Remarks = value.Remarks;
                await nodeInfoRepo.SaveChangesAsync(cancellationToken);
            }
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