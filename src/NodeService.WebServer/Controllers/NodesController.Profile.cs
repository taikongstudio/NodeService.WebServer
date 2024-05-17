namespace NodeService.WebServer.Controllers;

public partial class NodesController
{
    [HttpPost("/api/Nodes/{nodeId}/Profile/Update")]
    public async Task<ApiResponse<bool>> UpdateNodeInfoAsync(string nodeId, [FromBody] UpdateNodeProfileModel value)
    {
        var apiResponse = new ApiResponse<bool>();
        try
        {
            ArgumentNullException.ThrowIfNull(value, nameof(value));
            using var repo = _nodeInfoRepoFactory.CreateRepository();
            var nodeInfo = await repo.GetByIdAsync(nodeId);
            if (nodeInfo == null)
            {
                apiResponse.ErrorCode = -1;
                apiResponse.Message = "invalid node id";
                apiResponse.SetResult(false);
            }
            else
            {
                nodeInfo.Profile.TestInfo = value.TestInfo;
                nodeInfo.Profile.LabArea = value.LabArea;
                nodeInfo.Profile.LabName = value.LabName;
                nodeInfo.Profile.Usages = value.Usages;
                nodeInfo.Profile.Remarks = value.Remarks;
                await repo.SaveChangesAsync();
                apiResponse.SetResult(true);
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