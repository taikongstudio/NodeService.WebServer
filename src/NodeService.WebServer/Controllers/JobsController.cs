using System.Text;
using NodeService.Infrastructure.Logging;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Models;
using NodeService.WebServer.Services.Tasks;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public class JobsController : Controller
{
    readonly ApplicationRepositoryFactory<JobExecutionInstanceModel>  _taskInstanceRepositoryFactory;
    readonly ILogger<NodesController> _logger;
    readonly IMemoryCache _memoryCache;
    readonly INodeSessionService _nodeSessionService;
    readonly TaskExecutionInstanceInitializer _taskExecutionInstanceInitializer;
    readonly ExceptionCounter _exceptionCounter;
    readonly TaskLogCacheManager _taskLogCacheManager;

    public JobsController(
        ExceptionCounter exceptionCounter,
        ApplicationRepositoryFactory<JobExecutionInstanceModel> taskInstanceRepositoryFactory,
        INodeSessionService nodeSessionService,
        ILogger<NodesController> logger,
        IMemoryCache memoryCache,
        TaskExecutionInstanceInitializer taskExecutionInstanceInitializer,
        TaskLogCacheManager taskLogCacheManager)
    {
        _logger = logger;
        _taskInstanceRepositoryFactory = taskInstanceRepositoryFactory;
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

    [HttpGet("/api/jobs/instances/{id}/reinvoke")]
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

    [HttpPost("/api/jobs/instances/{id}/cancel")]
    public async Task<ApiResponse<JobExecutionInstanceModel>> CancelAsync(
        string id,
        [FromBody] TaskCancellationParameters parameters)
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
                    var result = taskLogCache.GetEntries(
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