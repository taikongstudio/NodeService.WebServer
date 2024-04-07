

using NodeService.Infrastructure.Interfaces;

namespace NodeService.WebServer.Controllers
{
    public partial class NodesController
    {

        [HttpGet("/api/nodes/{id}/filesystem/{**path}")]
        public async Task<ApiResponse<IEnumerable<FileSystemEntry>>> ListDirectoryAsync(string id, string path, [FromQuery] string? searchpattern)
        {
            ApiResponse<IEnumerable<FileSystemEntry>> apiResponse = new ApiResponse<IEnumerable<FileSystemEntry>>();
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
                    string requestId = Guid.NewGuid().ToString();
                    var fileSystemListRequest = new FileSystemListDirectoryRequest()
                    {
                        IncludeSubDirectories = false,
                        Directory = path,
                        RequestId = requestId,
                        SearchPattern = searchpattern,
                        Timeout = TimeSpan.FromSeconds(60)
                    };
                    FileSystemListDirectoryResponse? rsp = await this._nodeSessionService.SendMessageAsync<FileSystemListDirectoryRequest, FileSystemListDirectoryResponse>(
                        new NodeSessionId(id),
                        fileSystemListRequest);

                    apiResponse.ErrorCode = rsp.ErrorCode;
                    apiResponse.Message = rsp.Message;
                    apiResponse.Result = rsp.FileSystemObjects.Select(x => new FileSystemEntry()
                    {
                        CreationTime = x.CreationTime.ToDateTime(),
                        FullName = x.FullName,
                        LastWriteTime = x.LastWriteTime.ToDateTime(),
                        Length = x.Length,
                        Name = x.Name,
                        Type = x.Type,
                    });
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
}
