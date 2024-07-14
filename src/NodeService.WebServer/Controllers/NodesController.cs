using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public partial class NodesController : Controller
{
    private readonly ExceptionCounter _exceptionCounter;
    private readonly ILogger<NodesController> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly INodeSessionService _nodeSessionService;
    private readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepoFactory;
    private readonly ApplicationRepositoryFactory<NodePropertySnapshotModel> _nodePropertySnapshotRepoFactory;
    private readonly ApplicationRepositoryFactory<NodeStatusChangeRecordModel> _recordRepoFactory;
    private readonly ApplicationRepositoryFactory<PropertyBag> _propertyBagRepositoryFactory;
    private readonly ApplicationRepositoryFactory<TaskExecutionInstanceModel> _taskExecutionInstanceRepoFactory;
    private static readonly JsonSerializerOptions _jsonOptions;

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
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepositoryFactory,
        ApplicationRepositoryFactory<TaskExecutionInstanceModel> taskExecutionInstanceRepositoryFactory,
        ApplicationRepositoryFactory<NodePropertySnapshotModel> nodePropertySnapshotRepositoryFactory,
        ApplicationRepositoryFactory<NodeStatusChangeRecordModel> nodeStatusChangeRecordRepositoryFactory,
        ApplicationRepositoryFactory<PropertyBag> propertyBagRepositoryFactory,
        INodeSessionService nodeSessionService)
    {
        _logger = logger;
        _nodeSessionService = nodeSessionService;
        _memoryCache = memoryCache;
        _exceptionCounter = exceptionCounter;
        _nodeInfoRepoFactory = nodeInfoRepositoryFactory;
        _taskExecutionInstanceRepoFactory = taskExecutionInstanceRepositoryFactory;
        _nodePropertySnapshotRepoFactory = nodePropertySnapshotRepositoryFactory;
        _recordRepoFactory = nodeStatusChangeRecordRepositoryFactory;
        _propertyBagRepositoryFactory = propertyBagRepositoryFactory;
    }

    [HttpGet("/api/Nodes/List")]
    public async Task<PaginationResponse<NodeInfoModel>> QueryNodeListAsync(
        [FromQuery] QueryNodeListParameters queryParameters,
        CancellationToken cancellationToken = default)
    {
        var apiResponse = new PaginationResponse<NodeInfoModel>();
        try
        {
            await using var nodeInfoRepo = await _nodeInfoRepoFactory.CreateRepositoryAsync();

            ListQueryResult<NodeInfoModel> queryResult = default;
            if (queryParameters.IdList == null || queryParameters.IdList.Count == 0)
                queryResult = await nodeInfoRepo.PaginationQueryAsync(new NodeInfoSpecification(
                        queryParameters.AreaTag,
                        queryParameters.Status,
                        queryParameters.DeviceType,
                        queryParameters.Keywords,
                        queryParameters.SearchProfileProperties,
                        queryParameters.SortDescriptions),
                    queryParameters.PageSize,
                    queryParameters.PageIndex,
                    cancellationToken);
            else
                queryResult = await nodeInfoRepo.PaginationQueryAsync(new NodeInfoSpecification(
                        queryParameters.AreaTag,
                        queryParameters.Status,
                        queryParameters.DeviceType,
                        new DataFilterCollection<string>(DataFilterTypes.Include, queryParameters.IdList)),
                    queryParameters.PageSize,
                    queryParameters.PageIndex,
                    cancellationToken);
            if (queryParameters.IncludeProperties)
            {
                await using var propertyBagRepo = await _propertyBagRepositoryFactory.CreateRepositoryAsync();
                foreach (var nodeInfo in queryResult.Items)
                {
                    var propertyBag = await propertyBagRepo.GetByIdAsync(nodeInfo.GetPropertyBagId());
                    if (propertyBag == null || !propertyBag.TryGetValue("Value", out var value) ||
                        value is not string json) continue;

                    var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json, _jsonOptions);
                    nodeInfo.Properties = dict;
                }
            }
            
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


    [HttpGet("/api/Nodes/~/{id}")]
    public async Task<ApiResponse<NodeInfoModel>> QueryNodeInfoAsync(string id)
    {
        var apiResponse = new ApiResponse<NodeInfoModel>();
        try
        {
            await using var repo = await _nodeInfoRepoFactory.CreateRepositoryAsync();
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

    [HttpDelete("/api/Nodes/~/{id}/Delete")]
    public async Task<ApiResponse> DeleteNodeInfoAsync(string id)
    {
        var apiResponse = new ApiResponse<NodeInfoModel>();
        try
        {
            await using var repo = await _nodeInfoRepoFactory.CreateRepositoryAsync();
            var nodeInfo = await repo.GetByIdAsync(id);
            if (nodeInfo != null)
            {
                nodeInfo.Deleted = true;
                await repo.UpdateAsync(nodeInfo);
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
    public async Task<ApiResponse> AddOrUpdateNodeInfoAsync([FromBody] NodeInfoModel nodeInfo)
    {
        var apiResponse = new ApiResponse<NodeInfoModel>();
        try
        {
            await using var nodeInfoRepo = await _nodeInfoRepoFactory.CreateRepositoryAsync();
            await using var propertyBagRepo = await _propertyBagRepositoryFactory.CreateRepositoryAsync();
            var nodeInfoFromDb = await nodeInfoRepo.GetByIdAsync(nodeInfo.Id);
            if (nodeInfo.Properties != null)
            {
                var nodePropertyBagId = nodeInfo.GetPropertyBagId();
                var propertyBag = await propertyBagRepo.GetByIdAsync(nodePropertyBagId);
                if (propertyBag == null)
                {
                    propertyBag = new PropertyBag
                    {
                        { "Id", nodePropertyBagId },
                        { "Value", JsonSerializer.Serialize(nodeInfo.Properties) }
                    };
                    propertyBag["CreatedDate"] = DateTime.UtcNow;
                    await propertyBagRepo.AddAsync(propertyBag);
                }
                else
                {
                    propertyBag["Value"] = JsonSerializer.Serialize(nodeInfo.Properties);
                    await propertyBagRepo.UpdateAsync(propertyBag);
                }
            }

            if (nodeInfoFromDb == null)
            {
                nodeInfoFromDb = nodeInfo;
                await nodeInfoRepo.AddAsync(nodeInfoFromDb);
            }
            else
            {
                nodeInfoFromDb.Id = nodeInfo.Id;
                nodeInfoFromDb.Name = nodeInfo.Name;
                nodeInfoFromDb.DeviceType = nodeInfo.DeviceType;
                nodeInfoFromDb.Status = nodeInfo.Status;
                nodeInfoFromDb.Description = nodeInfo.Description;
                nodeInfoFromDb.Profile.Manufacturer = nodeInfo.Profile.Manufacturer;
                await nodeInfoRepo.UpdateAsync(nodeInfoFromDb);
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
}