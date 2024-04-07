using NodeService.Infrastructure.Models;
using NodeService.WebServer.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace NodeService.WebServer.Controllers
{
    public partial class NodesController
    {

        [HttpGet("/api/nodes/{id}/props/list")]
        public async Task<ApiResponse<IEnumerable<NodePropertyEntry>>> QueryNodePropsAsync(string id)
        {
            ApiResponse<IEnumerable<NodePropertyEntry>> apiResponse = new ApiResponse<IEnumerable<NodePropertyEntry>>();
            try
            {
                using var dbContext = _dbContextFactory.CreateDbContext();
                var nodeInfo = await dbContext.NodeInfoDbSet.FindAsync(id);
                if (nodeInfo == null)
                {
                    apiResponse.ErrorCode = -1;
                    apiResponse.Message = "invalid node id";
                }
                else
                {
                    var currentNodePropSnapshot = await dbContext.NodePropertySnapshotsDbSet.FindAsync(nodeInfo.LastNodePropertySnapshotId);
                    if (currentNodePropSnapshot != null)
                    {
                        var nodePropsCacheId = "NodeProps:" + nodeInfo.Id;
                        if (_memoryCache.TryGetValue<ConcurrentDictionary<string, string>>(nodePropsCacheId, out var propsDict))
                        {
                            if (propsDict != null)
                            {
                                currentNodePropSnapshot.NodeProperties = propsDict.Select(x => new NodePropertyEntry(x.Key, x.Value)).ToList();
                            }
                        }
                    }
                    apiResponse.Result = currentNodePropSnapshot?.NodeProperties;
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
}
