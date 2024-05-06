using System.Text;
using NodeService.Infrastructure.Logging;
using NodeService.WebServer.Models;
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
    private readonly ExceptionCounter _exceptionCounter;
    private readonly TaskLogCacheManager _taskLogCacheManager;

    public JobsController(
        ExceptionCounter exceptionCounter,
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
        _exceptionCounter = exceptionCounter;
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
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
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
                .OrderBy(x => x.FireTimeUtc)
                .AsSplitQuery();

            queryable = queryable.OrderBy(queryParameters.SortDescriptions);

                        var pageIndex = queryParameters.PageIndex - 1;
            var pageSize = queryParameters.PageSize;

            var totalCount = await queryable.CountAsync();

            var items = await queryable
                .Skip(pageIndex * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var pageCount = totalCount > 0 ? Math.DivRem(totalCount, pageSize, out var _) + 1 : 0;
            if (queryParameters.PageIndex > pageCount) queryParameters.PageIndex = pageCount;
            apiResponse.PageIndex = queryParameters.PageIndex;
            apiResponse.PageSize = queryParameters.PageSize;
            apiResponse.TotalCount = totalCount;
            apiResponse.Result = items;
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

    [HttpGet("/api/jobs/instances/{id}/reinvoke")]
    public async Task<ApiResponse<JobExecutionInstanceModel>> ReinvokeAsync(string id)
    {
        var apiResponse = new ApiResponse<JobExecutionInstanceModel>();
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
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
            _exceptionCounter.AddOrUpdate(ex);
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
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
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
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }

        return apiResponse;
    }


    [HttpGet("/api/jobs/instances/{taskId}/log")]
    public async Task<IActionResult> QueryTaskLogAsync(
        string taskId,
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        var apiResponse = new PaginationResponse<LogEntry>();
        try
        {
            apiResponse.TotalCount = _taskLogCacheManager.GetCache(taskId).Count;
            if (queryParameters.PageSize == 0)
            {
                await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
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
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
            apiResponse.ErrorCode = ex.HResult;
            apiResponse.Message = ex.Message;
        }

        return Json(apiResponse);
    }
}