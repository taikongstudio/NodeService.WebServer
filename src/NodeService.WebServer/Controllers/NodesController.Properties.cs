namespace NodeService.WebServer.Controllers;

public partial class NodesController
{
    [HttpGet("/api/Nodes/~/{nodeId}/props")]
    public async Task<ApiResponse<NodePropertySnapshotModel>> QueryNodePropsAsync(
        string nodeId,
        CancellationToken cancellationToken = default)
    {
        var apiResponse = new ApiResponse<NodePropertySnapshotModel>();
        try
        {
            await using var nodeRepo = await _nodeInfoRepoFactory.CreateRepositoryAsync();
            var nodeInfo = await nodeRepo.GetByIdAsync(nodeId);
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

                if ((model.NodeProperties == null || !model.NodeProperties.Any()) &&
                    nodeInfo.LastNodePropertySnapshotId != null)
                {
                    await using var nodePropRepo = await _nodePropertySnapshotRepoFactory.CreateRepositoryAsync(cancellationToken);
                    model = await nodePropRepo.GetByIdAsync(nodeInfo.LastNodePropertySnapshotId, cancellationToken);
                }

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