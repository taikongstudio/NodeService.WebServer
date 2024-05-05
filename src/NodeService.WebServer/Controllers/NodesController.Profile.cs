namespace NodeService.WebServer.Controllers;

public partial class NodesController
{
    [HttpPost("/api/nodes/{id}/profile/update")]
    public async Task<ApiResponse<bool>> UpdateNodeInfoAsync(string id, [FromBody] UpdateNodeProfileModel value)
    {
        var apiResponse = new ApiResponse<bool>();
        try
        {
            ArgumentNullException.ThrowIfNull(value, nameof(value));
            using var dbContext = _dbContextFactory.CreateDbContext();
            var nodeInfo = await dbContext.NodeInfoDbSet.FindAsync(id);
            if (nodeInfo == null)
            {
                apiResponse.ErrorCode = -1;
                apiResponse.Message = "invalid node id";
                apiResponse.Result = false;
            }
            else
            {
                nodeInfo.Profile.TestInfo = value.TestInfo;
                nodeInfo.Profile.LabArea = value.LabArea;
                nodeInfo.Profile.LabName = value.LabName;
                nodeInfo.Profile.Usages = value.Usages;
                nodeInfo.Profile.Remarks = value.Remarks;
                var changes = await dbContext.SaveChangesAsync();
                apiResponse.Result = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }

        return apiResponse;
    }
}