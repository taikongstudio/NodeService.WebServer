using NodeService.Infrastructure.NodeSessions;

namespace NodeService.WebServer.Controllers;

public partial class NodesController
{
    [HttpGet("/api/Nodes/{nodeId}/FileSystem/{**path}")]
    public async Task<ApiResponse<IEnumerable<FileSystemDirectoryEntry>>> ListDirectoryAsync(
        string nodeId,
        string path,
        [FromQuery] string? searchPattern)
    {
        var apiResponse = new ApiResponse<IEnumerable<FileSystemDirectoryEntry>>();
        try
        {
            await using var dbContext = await _nodeInfoRepoFactory.CreateRepositoryAsync();
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
                    SearchPattern = searchPattern,
                    Timeout = TimeSpan.FromSeconds(60)
                };
                var rsp = await _nodeSessionService
                    .SendMessageAsync<FileSystemListDirectoryRequest, FileSystemListDirectoryResponse>(
                        new NodeSessionId(nodeId),
                        fileSystemListRequest);

                apiResponse.ErrorCode = rsp.ErrorCode;
                apiResponse.Message = rsp.Message;
                apiResponse.SetResult(rsp.Directories.Select(x => new FileSystemDirectoryEntry
                {
                    CreationTime = x.CreationTime.ToDateTime(),
                    LastWriteTime = x.LastWriteTime.ToDateTime(),
                    LastAccessTime = x.LastAccessTime.ToDateTime(),
                    FullName = x.FullName,
                    Name = x.Name,
                    Attributes = x.Attributes,
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