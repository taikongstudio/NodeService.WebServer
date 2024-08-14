namespace NodeService.WebServer.Controllers
{
    public partial class NodesController
    {

        public async Task<ApiResponse<IEnumerable<NodeInfoModel>>> QueryNodeInfoListByExtendInfoAsyncAsync(
            NodeExtendInfo nodeExtendInfo,
            CancellationToken cancellationToken = default)
        {
            var rsp = new ApiResponse<IEnumerable<NodeInfoModel>>();
            try
            {
                var nodeInfoList = await _nodeInfoQueryService.QueryNodeInfoListByExtendInfoAsync(nodeExtendInfo, cancellationToken);
                rsp.SetResult(nodeInfoList);
            }
            catch (Exception ex)
            {
                rsp.SetError(ex.HResult, ex.Message);
            }
            return rsp;
        }

    }
}
