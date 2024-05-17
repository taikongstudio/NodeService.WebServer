using System.Text;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Logging;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.Tasks;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public class JobsController : Controller
{
    private readonly ExceptionCounter _exceptionCounter;
    private readonly ILogger<NodesController> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly INodeSessionService _nodeSessionService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IAsyncQueue<TaskCancellationParameters> _taskCancellationAsyncQueue;
    private readonly ApplicationRepositoryFactory<JobExecutionInstanceModel> _taskInstanceRepositoryFactory;
    private readonly TaskLogCacheManager _taskLogCacheManager;

    public JobsController(
        IServiceProvider serviceProvider,
        ExceptionCounter exceptionCounter,
        ApplicationRepositoryFactory<JobExecutionInstanceModel> taskInstanceRepositoryFactory,
        INodeSessionService nodeSessionService,
        ILogger<NodesController> logger,
        IMemoryCache memoryCache,
        TaskLogCacheManager taskLogCacheManager,
        IAsyncQueue<TaskCancellationParameters> taskCancellationAsyncQueue)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _taskInstanceRepositoryFactory = taskInstanceRepositoryFactory;
        _nodeSessionService = nodeSessionService;
        _memoryCache = memoryCache;
        _taskLogCacheManager = taskLogCacheManager;
        _exceptionCounter = exceptionCounter;
        _taskCancellationAsyncQueue = taskCancellationAsyncQueue;
    }

    [HttpGet("/api/Tasks/Instances/List")]
    public async Task<PaginationResponse<JobExecutionInstanceModel>> QueryTaskExecutionInstanceListAsync(
        [FromQuery] QueryTaskExecutionInstanceListParameters queryParameters
    )
    {
        var apiResponse = new PaginationResponse<JobExecutionInstanceModel>();
        try
        {
            if (queryParameters.BeginDateTime == null)
                queryParameters.BeginDateTime = DateTime.UtcNow.Date;
            if (queryParameters.EndDateTime == null)
                queryParameters.EndDateTime = DateTime.UtcNow.Date.AddDays(1).AddSeconds(-1);
            using var repo = _taskInstanceRepositoryFactory.CreateRepository();
            var queryResult = await repo.PaginationQueryAsync(new TaskExecutionInstanceSpecification(
                    queryParameters.Keywords,
                    queryParameters.Status,
                    queryParameters.NodeIdList,
                    queryParameters.TaskDefinitionIdList,
                    queryParameters.TaskExecutionInstanceIdList,
                    queryParameters.SortDescriptions),
                queryParameters.PageSize,
                queryParameters.PageIndex
            );
            apiResponse.SetResult(queryResult);
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

    [HttpGet("/api/Tasks/Instances/{id}/Reinvoke")]
    public async Task<ApiResponse<JobExecutionInstanceModel>> ReinvokeAsync(string id)
    {
        var apiResponse = new ApiResponse<JobExecutionInstanceModel>();
        try
        {
            using var repo = _taskInstanceRepositoryFactory.CreateRepository();
            var taskExecutionInstance = await repo.GetByIdAsync(id);
            if (taskExecutionInstance == null)
            {
                apiResponse.ErrorCode = -1;
                apiResponse.Message = "invalid job execution instance id";
            }
            else
            {
                taskExecutionInstance.ReinvokeTimes++;
                await repo.SaveChangesAsync();
                var nodeId = new NodeId(id);
                foreach (var nodeSessionId in _nodeSessionService.EnumNodeSessions(nodeId))
                {
                    var rsp = await _nodeSessionService.SendJobExecutionEventAsync(nodeSessionId,
                        taskExecutionInstance.ToReinvokeEvent());
                }
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

    [HttpPost("/api/Tasks/Instances/{id}/Cancel")]
    public async Task<ApiResponse<JobExecutionInstanceModel>> CancelAsync(
        string id,
        [FromBody] TaskCancellationParameters taskCancellationParameters)
    {
        var apiResponse = new ApiResponse<JobExecutionInstanceModel>();
        try
        {
            using var repo = _taskInstanceRepositoryFactory.CreateRepository();
            var taskExecutionInstance = await repo.GetByIdAsync(id);
            if (taskExecutionInstance == null)
            {
                apiResponse.ErrorCode = -1;
                apiResponse.Message = "invalid task execution instance id";
            }
            else
            {
                taskExecutionInstance.CancelTimes++;
                await repo.SaveChangesAsync();
                await _taskCancellationAsyncQueue.EnqueueAsync(new TaskCancellationParameters
                {
                    TaskExeuctionInstanceId = id
                });
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


    [HttpGet("/api/Tasks/Instances/{taskId}/log")]
    public async Task<IActionResult> QueryTaskLogAsync(
        string taskId,
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        var apiResponse = new PaginationResponse<LogEntry>();
        try
        {
            var taskLogCache = _taskLogCacheManager.GetCache(taskId);
            apiResponse.SetTotalCount(taskLogCache.Count);
            if (taskLogCache.IsTruncated)
            {
                apiResponse.ErrorCode = -1;
                apiResponse.Message = "log is truncated";
            }
            else if (taskLogCache.Count > 0)
            {
                if (queryParameters.PageSize == 0)
                {
                    using var repo = _taskInstanceRepositoryFactory.CreateRepository();
                    var taskExecutionInstance = await repo.GetByIdAsync(taskId);
                    var fileName = "x.log";
                    if (taskExecutionInstance == null)
                        fileName = $"{taskId}.log";
                    else
                        fileName = $"{taskExecutionInstance.Name}.log";
                    var result = taskLogCache.GetEntries();
                    var memoryStream = new MemoryStream();
                    using var streamWriter = new StreamWriter(memoryStream, leaveOpen: true);

                    foreach (var logEntry in result)
                        streamWriter.WriteLine(
                            $"{logEntry.DateTimeUtc.ToString(NodePropertyModel.DateTimeFormatString)} {logEntry.Value}");
                    await streamWriter.FlushAsync();
                    memoryStream.Position = 0;
                    return File(memoryStream, "text/plain", fileName);
                }

                var items = taskLogCache.GetEntries(
                        queryParameters.PageIndex,
                        queryParameters.PageSize)
                    .OrderBy(static x => x.Index).ToArray();
                apiResponse.SetResult(items);
            }
            else
            {
                apiResponse.SetResult([]);
            }


            apiResponse.SetPageIndex(queryParameters.PageIndex);
            apiResponse.SetPageSize(queryParameters.PageSize);
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