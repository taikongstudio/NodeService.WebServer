using NodeService.Infrastructure.Models;
using NodeService.WebServer.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace NodeService.WebServer.Controllers
{
    public partial class NodesController
    {

        [HttpGet("/api/nodes/{id}/props")]
        public async Task<ApiResponse<NodePropertySnapshotModel>> QueryNodePropsAsync(string id)
        {
            ApiResponse<NodePropertySnapshotModel> apiResponse = new ApiResponse<NodePropertySnapshotModel>();
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
                    NodePropertySnapshotModel model = new NodePropertySnapshotModel();
                    var nodePropsCacheId = "NodeProps:" + nodeInfo.Id;
                    if (_memoryCache.TryGetValue<ConcurrentDictionary<string, string>>(nodePropsCacheId, out var propsDict))
                    {
                        if (propsDict != null)
                        {
                            model.CreationDateTime = DateTime.UtcNow;
                            model.NodeProperties = propsDict.Select(NodePropertyEntry.From).ToList();
                        }
                    }
                    if (model.NodeProperties == null)
                    {
                        model = await dbContext.NodePropertySnapshotsDbSet.FindAsync(nodeInfo.LastNodePropertySnapshotId);
                    }

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
}
