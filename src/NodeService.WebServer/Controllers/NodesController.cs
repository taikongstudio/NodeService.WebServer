using Ardalis.Specification.EntityFrameworkCore;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.Models;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using System.IO;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public partial class NodesController : Controller
{
    readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepositoryFactory;
    readonly ApplicationRepositoryFactory<JobExecutionInstanceModel> _taskExecutionInstanceRepositoryFactory;
    readonly ApplicationRepositoryFactory<NodePropertySnapshotModel> _nodePropertySnapshotRepositoryFactory;
    readonly ILogger<NodesController> _logger;
    readonly IMemoryCache _memoryCache;
    readonly INodeSessionService _nodeSessionService;
    readonly IVirtualFileSystem _virtualFileSystem;
    readonly WebServerOptions _webServerOptions;
    readonly ExceptionCounter _exceptionCounter;

    public NodesController(
        ExceptionCounter exceptionCounter,
        ILogger<NodesController> logger,
        IMemoryCache memoryCache,
        IOptionsSnapshot<WebServerOptions> webServerOptions,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepositoryFactory,
        ApplicationRepositoryFactory<JobExecutionInstanceModel> taskExecutionInstanceRepositoryFactory,
        ApplicationRepositoryFactory<NodePropertySnapshotModel> nodePropertySnapshotRepositoryFactory,
        INodeSessionService nodeSessionService)
    {
        _logger = logger;
        _nodeInfoRepositoryFactory = nodeInfoRepositoryFactory;
        _taskExecutionInstanceRepositoryFactory = taskExecutionInstanceRepositoryFactory;
        _nodePropertySnapshotRepositoryFactory = nodePropertySnapshotRepositoryFactory;
        _nodeSessionService = nodeSessionService;
        _memoryCache = memoryCache;
        _webServerOptions = webServerOptions.Value;
        _exceptionCounter = exceptionCounter;
    }

    [HttpGet("/api/nodes/list")]
    public async Task<PaginationResponse<NodeInfoModel>> QueryNodeListAsync(
        [FromQuery] QueryNodeListParameters queryParameters,
        CancellationToken cancellationToken = default)
    {
        var apiResponse = new PaginationResponse<NodeInfoModel>();
        try
        {
            using var repo = _nodeInfoRepositoryFactory.CreateRepository();
            ListQueryResult<NodeInfoModel> queryResult = default;
            if (queryParameters.IdList == null || queryParameters.IdList.Count == 0)
            {
                queryResult = await repo.PaginationQueryAsync(new NodeInfoSpecification(
                    queryParameters.AreaTag,
                    queryParameters.Status,
                    queryParameters.Keywords,
                    queryParameters.SortDescriptions),
                    queryParameters.PageSize,
                    queryParameters.PageIndex,
                    cancellationToken);
            }
            else
            {
                queryResult = await repo.PaginationQueryAsync(new NodeInfoSpecification(
                    queryParameters.AreaTag,
                    queryParameters.Status,
                    new DataFilterCollection<string>(DataFilterTypes.Include, queryParameters.IdList)),
                    queryParameters.PageSize,
                    queryParameters.PageIndex,
                    cancellationToken);
            }

            apiResponse.SetResult(queryResult);
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogInformation(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }

        return apiResponse;
    }



    [HttpGet("/api/nodes/{id}")]
    public async Task<ApiResponse<NodeInfoModel>> QueryNodeInfoAsync(string id)
    {
        var apiResponse = new ApiResponse<NodeInfoModel>();
        try
        {
            using var repo = _nodeInfoRepositoryFactory.CreateRepository();
            var nodeInfo = await repo.GetByIdAsync(id);
            apiResponse.SetResult(nodeInfo);
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