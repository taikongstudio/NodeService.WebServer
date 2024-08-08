using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Models;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.DataQueue;
using System.Threading;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public partial class NodesController : Controller
{
    readonly ExceptionCounter _exceptionCounter;
    readonly ILogger<NodesController> _logger;
    readonly IMemoryCache _memoryCache;
    readonly INodeSessionService _nodeSessionService;
    readonly NodeInfoQueryService _nodeInfoQueryService;
    readonly ApplicationRepositoryFactory<NodeStatusChangeRecordModel> _recordRepoFactory;
    private readonly NodeStatusChangeQueryService _nodeStatusChangeQueryService;
    static readonly JsonSerializerOptions _jsonOptions;

    static NodesController()
    {
        _jsonOptions = _jsonOptions = new JsonSerializerOptions()
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public NodesController(
        ExceptionCounter exceptionCounter,
        ILogger<NodesController> logger,
        IMemoryCache memoryCache,
        NodeInfoQueryService nodeInfoQueryService,
        NodeStatusChangeQueryService nodeStatusChangeQueryService,
        ApplicationRepositoryFactory<TaskExecutionInstanceModel> taskExecutionInstanceRepositoryFactory,
        ApplicationRepositoryFactory<NodeStatusChangeRecordModel> nodeStatusChangeRecordRepositoryFactory,
        ApplicationRepositoryFactory<PropertyBag> propertyBagRepositoryFactory,
        INodeSessionService nodeSessionService)
    {
        _logger = logger;
        _nodeSessionService = nodeSessionService;
        _memoryCache = memoryCache;
        _exceptionCounter = exceptionCounter;
        _nodeInfoQueryService = nodeInfoQueryService;
        _recordRepoFactory = nodeStatusChangeRecordRepositoryFactory;
        _nodeStatusChangeQueryService = nodeStatusChangeQueryService;
    }

    [HttpGet("/api/Nodes/List")]
    public async Task<PaginationResponse<NodeInfoModel>> QueryNodeListAsync(
        [FromQuery] QueryNodeListParameters queryParameters,
        CancellationToken cancellationToken = default)
    {
        var apiResponse = new PaginationResponse<NodeInfoModel>();
        try
        {
            var queryResult = await _nodeInfoQueryService.QueryNodeInfoListByQueryParameters(queryParameters, cancellationToken);
            
            foreach (var item in queryResult.Items)
            {
                item.PingReplyInfo = _nodeSessionService.GetNodeLastPingReplyInfo(new NodeSessionId(item.Id));
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

    [HttpGet("/api/Nodes/List/Export")]
    public async Task<IActionResult> ExportNodeListAsync(CancellationToken cancellationToken = default)
    {
        var apiResponse = new PaginationResponse<NodeInfoModel>();
        try
        {
            var stream = await _nodeInfoQueryService.ExportNodeListAsync(new QueryNodeListParameters()
            {
                AreaTag = AreaTags.Any,
                DeviceType = NodeDeviceType.All,
                IncludeProperties = false,
                PageIndex = 1,
                PageSize = int.MaxValue,
                Status = NodeStatus.All,
                SearchProfileProperties = false
            }, cancellationToken);
            stream.Position = 0;
            return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "NodeList.xlsx");
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogInformation(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }

        return Json(apiResponse);
    }


    [HttpGet("/api/Nodes/~/{id}")]
    public async Task<ApiResponse<NodeInfoModel>> QueryNodeInfoAsync(string id, CancellationToken cancellationToken = default)
    {
        var apiResponse = new ApiResponse<NodeInfoModel>();
        try
        {
            var nodeInfo = await _nodeInfoQueryService.QueryNodeInfoByIdAsync(id, false, cancellationToken);
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

    [HttpDelete("/api/Nodes/~/{id}/Delete")]
    public async Task<ApiResponse> DeleteNodeInfoAsync(string id, CancellationToken cancellationToken = default)
    {
        var apiResponse = new ApiResponse<NodeInfoModel>();
        try
        {
            var nodeInfo = await _nodeInfoQueryService.QueryNodeInfoByIdAsync(id, false, cancellationToken);
            if (nodeInfo != null)
            {
                nodeInfo.Deleted = true;
                await _nodeInfoQueryService.UpdateNodeInfoListAsync([nodeInfo]);
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

    [HttpPost("/api/Nodes/AddOrUpdate")]
    public async Task<ApiResponse> AddOrUpdateNodeInfoAsync(
        [FromBody] NodeInfoModel nodeInfo,
        CancellationToken cancellationToken = default)
    {
        var apiResponse = new ApiResponse<NodeInfoModel>();
        try
        {
            await _nodeInfoQueryService.AddOrUpdateNodeInfoAsync(
                nodeInfo,
                cancellationToken);
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