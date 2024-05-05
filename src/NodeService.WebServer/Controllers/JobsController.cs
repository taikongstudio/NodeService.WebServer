using System.Text;
using NodeService.Infrastructure.Logging;
using NodeService.WebServer.Services.Tasks;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public class JobsController : Controller
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly ILogger<NodesController> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly INodeSessionService _nodeSessionService;
    private readonly TaskExecutionInstanceInitializer _taskExecutionInstanceInitializer;
    private readonly TaskLogCacheManager _taskLogCacheManager;

    public JobsController(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        INodeSessionService nodeSessionService,
        ILogger<NodesController> logger,
        IMemoryCache memoryCache,
        TaskExecutionInstanceInitializer taskExecutionInstanceInitializer,
        TaskLogCacheManager taskLogCacheManager)
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _nodeSessionService = nodeSessionService;
        _memoryCache = memoryCache;
        _taskLogCacheManager = taskLogCacheManager;
        _taskExecutionInstanceInitializer = taskExecutionInstanceInitializer;
    }

    [HttpGet("/api/jobs/instances/list")]
    public async Task<PaginationResponse<JobExecutionInstanceModel>> QueryTaskExecutionInstanceListAsync(
        [FromQuery] QueryTaskExecutionInstanceListParameters queryParameters
    )
    {
        var apiResponse = new PaginationResponse<JobExecutionInstanceModel>();
        try
        {
            if (queryParameters.BeginDateTime == null)
                queryParameters.BeginDateTime = DateTime.UtcNow.ToUniversalTime().Date;
            if (queryParameters.EndDateTime == null)
                queryParameters.EndDateTime = DateTime.UtcNow.ToUniversalTime().Date.AddDays(1).AddSeconds(-1);
            using var dbContext = _dbContextFactory.CreateDbContext();
            var beginTime = queryParameters.BeginDateTime.Value;
            var endTime = queryParameters.EndDateTime.Value;
            IQueryable<JobExecutionInstanceModel> queryable = dbContext.JobExecutionInstancesDbSet;
            if (queryParameters.NodeIdList.Any())
                queryable = queryable.Where(x => queryParameters.NodeIdList.Contains(x.NodeInfoId));

            if (queryParameters.Status != JobExecutionReport.Types.JobExecutionStatus.Unknown)
                queryable = queryable.Where(x => x.Status == queryParameters.Status);

            if (queryParameters.JobScheduleConfigIdList.Any())
                queryable = queryable.Where(
                    x => queryParameters.JobScheduleConfigIdList.Contains(x.JobScheduleConfigId));

            queryable = queryable
                .Where(x => x.FireTimeUtc >= beginTime && x.FireTimeUtc < endTime)
                .AsSplitQuery();

            var isFirstOrder = true;
            IOrderedQueryable<JobExecutionInstanceModel> orderedQueryable = null;
            foreach (var sortDescription in queryParameters.SortDescriptions)
            {
                var path = sortDescription.Name;
                switch (sortDescription.Name)
                {
                    case nameof(NodeInfoModel.Name):
                    case nameof(NodeInfoModel.Status):
                        break;
                    default:
                        path = $"{nameof(NodeInfoModel.Profile)}.{sortDescription.Name}";
                        break;
                }

                if (sortDescription.Direction == "ascend" || string.IsNullOrEmpty(sortDescription.Direction))
                {
                    if (isFirstOrder)
                        orderedQueryable = queryable.OrderByColumnUsing(path, sortDescription.Direction);
                    else
                        orderedQueryable = orderedQueryable.ThenByColumnUsing(path, sortDescription.Direction);
                }
                else if (sortDescription.Direction == "descend")
                {
                    if (isFirstOrder)
                        orderedQueryable = queryable.OrderByColumnUsing(path, sortDescription.Direction);
                    else
                        orderedQueryable = orderedQueryable.ThenByColumnUsing(path, sortDescription.Direction);
                }

                isFirstOrder = false;
            }

            if (orderedQueryable != null) queryable = orderedQueryable;

            var pageIndex = queryParameters.PageIndex - 1;
            var pageSize = queryParameters.PageSize;

            var totalCount = await queryable.CountAsync();

            apiResponse.TotalCount = totalCount;

            var items = await queryable
                .Skip(pageIndex * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var pageCount = totalCount > 0 ? Math.DivRem(totalCount, pageSize, out var _) + 1 : 0;
            if (queryParameters.PageIndex > pageCount) queryParameters.PageIndex = pageCount;
            apiResponse.PageIndex = queryParameters.PageIndex;
            apiResponse.PageSize = queryParameters.PageSize;
            apiResponse.Result = items;

            apiResponse.Result = items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }

        return apiResponse;
    }

    [HttpGet("/api/jobs/instances/{id}/reinvoke")]
    public async Task<ApiResponse<JobExecutionInstanceModel>> ReinvokeAsync(string id)
    {
        var apiResponse = new ApiResponse<JobExecutionInstanceModel>();
        try
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var jobExecutionInstance = await dbContext.JobExecutionInstancesDbSet.FindAsync(id);
            if (jobExecutionInstance == null)
            {
                apiResponse.ErrorCode = -1;
                apiResponse.Message = "invalid job execution instance id";
            }
            else
            {
                jobExecutionInstance.ReinvokeTimes++;
                var nodeId = new NodeId(id);
                foreach (var nodeSessionId in _nodeSessionService.EnumNodeSessions(nodeId))
                {
                    var rsp = await _nodeSessionService.SendJobExecutionEventAsync(nodeSessionId,
                        jobExecutionInstance.ToReinvokeEvent());
                }
            }

            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }

        return apiResponse;
    }

    [HttpPost("/api/jobs/instances/{id}/cancel")]
    public async Task<ApiResponse<JobExecutionInstanceModel>> CancelAsync(
        string id,
        [FromBody] TaskCancellationParameters parameters)
    {
        var apiResponse = new ApiResponse<JobExecutionInstanceModel>();
        try
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var taskExecutionInstance = await dbContext.JobExecutionInstancesDbSet.FindAsync(id);
            if (taskExecutionInstance == null)
            {
                apiResponse.ErrorCode = -1;
                apiResponse.Message = "invalid task execution instance id";
            }
            else
            {
                taskExecutionInstance.CancelTimes++;
                await dbContext.SaveChangesAsync();
                await _taskExecutionInstanceInitializer.TryCancelAsync(taskExecutionInstance.Id);
                var rsp = await _nodeSessionService.SendJobExecutionEventAsync(
                    new NodeSessionId(taskExecutionInstance.NodeInfoId),
                    taskExecutionInstance.ToCancelEvent());
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


    [HttpGet("/api/jobs/instances/{taskId}/log")]
    public async Task<IActionResult> QueryTaskLogAsync(string taskId, PaginationQueryParameters queryParameters)
    {
        var apiResponse = new PaginationResponse<LogEntry>();
        try
        {
            apiResponse.TotalCount = _taskLogCacheManager.GetCache(taskId).Count;
            if (queryParameters.PageSize == 0)
            {
                using var dbContext = _dbContextFactory.CreateDbContext();
                var instance = await dbContext.JobExecutionInstancesDbSet.FirstOrDefaultAsync(x => x.Id == taskId);
                var fileName = "x.log";
                if (instance == null)
                    fileName = $"{taskId}.log";
                else
                    fileName = $"{instance.Name}.log";
                var result = _taskLogCacheManager.GetCache(taskId).GetEntries(
                    queryParameters.PageIndex,
                    queryParameters.PageSize);
                var memoryStream = new MemoryStream();
                using var streamWriter = new StreamWriter(memoryStream, Encoding.UTF8, leaveOpen: true);

                foreach (var logEntry in result)
                    streamWriter.WriteLine(
                        $"{logEntry.DateTimeUtc.ToString(NodePropertyModel.DateTimeFormatString)} {logEntry.Value}");
                await streamWriter.FlushAsync();
                memoryStream.Position = 0;
                return File(memoryStream, "text/plain", fileName);
            }

            apiResponse.Result = _taskLogCacheManager.GetCache(taskId).GetEntries(
                    queryParameters.PageIndex,
                    queryParameters.PageSize)
                .OrderBy(static x => x.Index);

            apiResponse.PageIndex = queryParameters.PageIndex;
            apiResponse.PageSize = queryParameters.PageSize;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }

        return Json(apiResponse);
    }
}