using NodeService.Infrastructure.Models;
using System.IO;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public partial class NodesController : Controller
{
    private readonly IAsyncQueue<TaskScheduleMessage> _asyncQueue;
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ILogger<NodesController> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly INodeSessionService _nodeSessionService;
    private readonly IVirtualFileSystem _virtualFileSystem;
    private readonly WebServerOptions _webServerOptions;
    private readonly ExceptionCounter _exceptionCounter;

    public NodesController(
        ExceptionCounter exceptionCounter,
        ILogger<NodesController> logger,
        IMemoryCache memoryCache,
        IOptionsSnapshot<WebServerOptions> webServerOptions,
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        IAsyncQueue<TaskScheduleMessage> jobScheduleServiceMessageQueue,
        INodeSessionService nodeSessionService)
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _asyncQueue = jobScheduleServiceMessageQueue;
        _nodeSessionService = nodeSessionService;
        _memoryCache = memoryCache;
        _webServerOptions = webServerOptions.Value;
        _exceptionCounter = exceptionCounter;
    }

    [HttpGet("/api/nodes/list")]
    public async Task<PaginationResponse<NodeInfoModel>> QueryNodeListAsync(
        [FromQuery] QueryNodeListParameters queryParameters)
    {
        var apiResponse = new PaginationResponse<NodeInfoModel>();
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            var pageIndex = queryParameters.PageIndex - 1;
            var pageSize = queryParameters.PageSize;

            var queryable = dbContext.NodeInfoDbSet.AsQueryable();

            if (queryParameters.AreaTag != "*")
                queryable = queryable.Where(x => x.Profile.FactoryName == queryParameters.AreaTag);
            if (queryParameters.Status != NodeStatus.All)
                queryable = queryable.Where(x => x.Status == queryParameters.Status);
            if (queryParameters.IdList != null && queryParameters.IdList.Any())
                queryable = queryable.Where(x => queryParameters.IdList.Contains(x.Id));
            if (queryParameters.Keywords != null)
                queryable = queryable.Where(x => x.Name == queryParameters.Keywords ||
                                                 x.Name.Contains(queryParameters.Keywords)
                                                 ||
                                                 x.Profile.IpAddress == queryParameters.Keywords ||
                                                 x.Profile.IpAddress.Contains(queryParameters.Keywords)
                                                 ||
                                                 x.Profile.ClientVersion == queryParameters.Keywords ||
                                                 x.Profile.ClientVersion.Contains(queryParameters.Keywords)
                                                 ||
                                                 x.Profile.IpAddress == queryParameters.Keywords ||
                                                 x.Profile.IpAddress.Contains(queryParameters.Keywords)
                                                 ||
                                                 x.Profile.Usages == queryParameters.Keywords ||
                                                 x.Profile.Usages.Contains(queryParameters.Keywords)
                                                 ||
                                                 x.Profile.Remarks == queryParameters.Keywords ||
                                                 x.Profile.Remarks.Contains(queryParameters.Keywords));

            queryable = queryable
            .Include(x => x.Profile)
                .AsSplitQuery();

            queryable = queryable.OrderBy(queryParameters.SortDescriptions, static (name) =>
            {
                return name switch
                {
                    nameof(NodeInfoModel.Name) or nameof(NodeInfoModel.Status) => name,
                    _ => $"{nameof(NodeInfoModel.Profile)}.{name}",
                };
            });
            var totalCount = await queryable.CountAsync();

            var startIndex = pageIndex * pageSize;

            var skipCount = totalCount > startIndex ? startIndex : 0;


            var items = await queryable
                .Skip(skipCount)
                .Take(pageSize)
                .ToArrayAsync();
            var pageCount = totalCount > 0 ? Math.DivRem(totalCount, pageSize, out var _) + 1 : 0;
            if (queryParameters.PageIndex > pageCount) queryParameters.PageIndex = pageCount;
            apiResponse.TotalCount = totalCount;
            apiResponse.PageIndex = queryParameters.PageIndex;
            apiResponse.PageSize = queryParameters.PageSize;
            apiResponse.Result = items;
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

    [HttpGet("/api/nodes/{id}")]
    public async Task<ApiResponse<NodeInfoModel>> QueryNodeInfoAsync(string id)
    {
        var apiResponse = new ApiResponse<NodeInfoModel>();
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            var nodeInfo =
                await dbContext
                    .NodeInfoDbSet
                    .FindAsync(id);
            apiResponse.Result = nodeInfo;
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