﻿namespace NodeService.WebServer.Controllers;

public partial class NodesController
{
    [HttpGet("/api/nodes/{id}/props")]
    public async Task<ApiResponse<NodePropertySnapshotModel>> QueryNodePropsAsync(string id)
    {
        var apiResponse = new ApiResponse<NodePropertySnapshotModel>();
        try
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
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

                apiResponse.Result = model;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.ToString();
        }

        return apiResponse;
    }
}