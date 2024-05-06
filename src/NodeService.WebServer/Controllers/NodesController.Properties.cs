namespace NodeService.WebServer.Controllers;

public partial class NodesController
{
    [HttpGet("/api/nodes/{id}/props")]
    public async Task<ApiResponse<NodePropertySnapshotModel>> QueryNodePropsAsync(string id)
    {
        var apiResponse = new ApiResponse<NodePropertySnapshotModel>();
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            var nodeInfo = await dbContext.NodeInfoDbSet.FirstOrDefaultAsync(x => x.Id == id);
            if (nodeInfo == null)
            {
                apiResponse.ErrorCode = -1;
                apiResponse.Message = "invalid node id";
            }
            else
            {
                var model = new NodePropertySnapshotModel();
                var nodePropsCacheId = "NodeProps:" + nodeInfo.Id;
                if (_memoryCache.TryGetValue<ConcurrentDictionary<string, string>>(nodePropsCacheId, out var propsDict))
                    if (propsDict != null)
                    {
                        model.CreationDateTime = DateTime.UtcNow;
                        model.NodeProperties = propsDict.Select(NodePropertyEntry.From).ToList();
                    }

                if (model.NodeProperties == null || !model.NodeProperties.Any())
                    model = await dbContext.NodePropertiesSnapshotsDbSet.FindAsync(nodeInfo.LastNodePropertySnapshotId);

                apiResponse.SetResult(model);
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.ToString();
        }

        return apiResponse;
    }
}