using NodeService.Infrastructure.NodeSessions;

namespace NodeService.WebServer.Controllers;

public partial class NodesController
{
    [HttpGet("/api/nodes/{nodeId}/filesystem/{**path}")]
    public async Task<ApiResponse<IEnumerable<FileSystemEntry>>> ListDirectoryAsync(string nodeId, string path,
        [FromQuery] string? searchpattern)
    {
        var apiResponse = new ApiResponse<IEnumerable<FileSystemEntry>>();
        try
        {
            using var dbContext = _nodeInfoRepositoryFactory.CreateRepository();
            var nodeInfo = await dbContext.GetByIdAsync(nodeId);
            if (nodeInfo == null)
            {
                apiResponse.ErrorCode = -1;
                apiResponse.Message = "invalid node id";
            }
            else
            {
                var requestId = Guid.NewGuid().ToString();
                var fileSystemListRequest = new FileSystemListDirectoryRequest
                {
                    IncludeSubDirectories = false,
                    Directory = path,
                    RequestId = requestId,
                    SearchPattern = searchpattern,
                    Timeout = TimeSpan.FromSeconds(60)
                };
                var rsp = await _nodeSessionService
                    .SendMessageAsync<FileSystemListDirectoryRequest, FileSystemListDirectoryResponse>(
                        new NodeSessionId(nodeId),
                        fileSystemListRequest);

                apiResponse.ErrorCode = rsp.ErrorCode;
                apiResponse.Message = rsp.Message;
                apiResponse.SetResult(rsp.FileSystemObjects.Select(x => new FileSystemEntry
                {
                    CreationTime = x.CreationTime.ToDateTime(),
                    FullName = x.FullName,
                    LastWriteTime = x.LastWriteTime.ToDateTime(),
                    Length = x.Length,
                    Name = x.Name,
                    Type = x.Type
                }));
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