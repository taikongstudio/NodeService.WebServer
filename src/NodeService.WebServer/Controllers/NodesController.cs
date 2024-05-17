using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public partial class NodesController : Controller
{
    readonly ExceptionCounter _exceptionCounter;
    readonly ILogger<NodesController> _logger;
    readonly IMemoryCache _memoryCache;
    readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepoFactory;
    readonly ApplicationRepositoryFactory<NodePropertySnapshotModel> _nodePropertySnapshotRepoFactory;
    readonly ApplicationRepositoryFactory<NodeStatusChangeRecordModel> _recordRepoFactory;
    readonly ApplicationRepositoryFactory<JobExecutionInstanceModel> _taskExecutionInstanceRepoFactory;
    readonly INodeSessionService _nodeSessionService;
    readonly IVirtualFileSystem _virtualFileSystem;
    readonly WebServerOptions _webServerOptions;

    public NodesController(
        ExceptionCounter exceptionCounter,
        ILogger<NodesController> logger,
        IMemoryCache memoryCache,
        IOptionsSnapshot<WebServerOptions> webServerOptions,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepositoryFactory,
        ApplicationRepositoryFactory<JobExecutionInstanceModel> taskExecutionInstanceRepositoryFactory,
        ApplicationRepositoryFactory<NodePropertySnapshotModel> nodePropertySnapshotRepositoryFactory,
        ApplicationRepositoryFactory<NodeStatusChangeRecordModel> nodeStatusChangeRecordRepositoryFactory,
        INodeSessionService nodeSessionService)
    {
        _logger = logger;
        _nodeInfoRepoFactory = nodeInfoRepositoryFactory;
        _taskExecutionInstanceRepoFactory = taskExecutionInstanceRepositoryFactory;
        _nodePropertySnapshotRepoFactory = nodePropertySnapshotRepositoryFactory;
        _recordRepoFactory = nodeStatusChangeRecordRepositoryFactory;
        _nodeSessionService = nodeSessionService;
        _memoryCache = memoryCache;
        _webServerOptions = webServerOptions.Value;
        _exceptionCounter = exceptionCounter;
    }

    [HttpGet("/api/Nodes/List")]
    public async Task<PaginationResponse<NodeInfoModel>> QueryNodeListAsync(
        [FromQuery] QueryNodeListParameters queryParameters,
        CancellationToken cancellationToken = default)
    {
        var apiResponse = new PaginationResponse<NodeInfoModel>();
        try
        {
            using var repo = _nodeInfoRepoFactory.CreateRepository();
            ListQueryResult<NodeInfoModel> queryResult = default;
            if (queryParameters.IdList == null || queryParameters.IdList.Count == 0)
                queryResult = await repo.PaginationQueryAsync(new NodeInfoSpecification(
                        queryParameters.AreaTag,
                        queryParameters.Status,
                        queryParameters.Keywords,
                        queryParameters.SearchProfileProperties,
                        queryParameters.SortDescriptions),
                    queryParameters.PageSize,
                    queryParameters.PageIndex,
                    cancellationToken);
            else
                queryResult = await repo.PaginationQueryAsync(new NodeInfoSpecification(
                        queryParameters.AreaTag,
                        queryParameters.Status,
                        new DataFilterCollection<string>(DataFilterTypes.Include, queryParameters.IdList)),
                    queryParameters.PageSize,
                    queryParameters.PageIndex,
                    cancellationToken);

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


    [HttpGet("/api/Nodes/{id}")]
    public async Task<ApiResponse<NodeInfoModel>> QueryNodeInfoAsync(string id)
    {
        var apiResponse = new ApiResponse<NodeInfoModel>();
        try
        {
            using var repo = _nodeInfoRepoFactory.CreateRepository();
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