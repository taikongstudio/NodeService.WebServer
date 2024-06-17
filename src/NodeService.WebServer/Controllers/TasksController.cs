using System.Text;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Logging;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.Tasks;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public class TasksController : Controller
{
    private readonly ExceptionCounter _exceptionCounter;
    private readonly ILogger<NodesController> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly INodeSessionService _nodeSessionService;
    private readonly IServiceProvider _serviceProvider;
    private readonly BatchQueue<TaskCancellationParameters> _taskCancellationAsyncQueue;
    readonly ApplicationRepositoryFactory<TaskExecutionInstanceModel> _taskInstanceRepositoryFactory;
    readonly ApplicationRepositoryFactory<TaskLogModel> _taskLogRepoFactory;

    public TasksController(
        IServiceProvider serviceProvider,
        ExceptionCounter exceptionCounter,
        ApplicationRepositoryFactory<TaskExecutionInstanceModel> taskInstanceRepositoryFactory,
        INodeSessionService nodeSessionService,
        ILogger<NodesController> logger,
        IMemoryCache memoryCache,
        ApplicationRepositoryFactory<TaskLogModel> taskLogRepoFactory,
        BatchQueue<TaskCancellationParameters> taskCancellationAsyncQueue)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _taskInstanceRepositoryFactory = taskInstanceRepositoryFactory;
        _nodeSessionService = nodeSessionService;
        _memoryCache = memoryCache;
        _taskLogRepoFactory = taskLogRepoFactory;
        _exceptionCounter = exceptionCounter;
        _taskCancellationAsyncQueue = taskCancellationAsyncQueue;
    }

    [HttpGet("/api/Tasks/Instances/List")]
    public async Task<PaginationResponse<TaskExecutionInstanceModel>> QueryTaskExecutionInstanceListAsync(
        [FromQuery] QueryTaskExecutionInstanceListParameters queryParameters
    )
    {
        var apiResponse = new PaginationResponse<TaskExecutionInstanceModel>();
        try
        {
            using var repo = _taskInstanceRepositoryFactory.CreateRepository();
            var queryResult = await repo.PaginationQueryAsync(new TaskExecutionInstanceSpecification(
                    queryParameters.Keywords,
                    queryParameters.Status,
                    queryParameters.NodeIdList,
                    queryParameters.TaskDefinitionIdList,
                    queryParameters.TaskExecutionInstanceIdList,
                    queryParameters.BeginDateTime,
                    queryParameters.EndDateTime,
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
    public async Task<ApiResponse<TaskExecutionInstanceModel>> ReinvokeAsync(string id)
    {
        var apiResponse = new ApiResponse<TaskExecutionInstanceModel>();
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
                    var rsp = await _nodeSessionService.SendTaskExecutionEventAsync(nodeSessionId,
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
    public async Task<ApiResponse<TaskExecutionInstanceModel>> CancelAsync(
        string id,
        [FromBody] TaskCancellationParameters taskCancellationParameters)
    {
        var apiResponse = new ApiResponse<TaskExecutionInstanceModel>();
        try
        {
            await _taskCancellationAsyncQueue.SendAsync(new TaskCancellationParameters
            {
                TaskExeuctionInstanceId = id
            });
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


    [HttpGet("/api/Tasks/Instances/{taskId}/Log")]
    public async Task<IActionResult> QueryTaskLogAsync(
        string taskId,
        [FromQuery] PaginationQueryParameters queryParameters)
    {
        var apiResponse = new PaginationResponse<LogEntry>();
        try
        {
            using var taskLogRepo = _taskLogRepoFactory.CreateRepository();
            var taskInfoLog = await taskLogRepo.FirstOrDefaultAsync(new TaskLogSpecification(taskId, 0));
            if (taskInfoLog == null)
            {
                apiResponse.SetResult([]);
            }
            else
            {
                int totalLogCount = taskInfoLog.ActualSize;
                apiResponse.SetTotalCount(totalLogCount);
                if (totalLogCount > 0)
                {
                    if (queryParameters.PageIndex == 0)
                    {
                        using var repo = _taskInstanceRepositoryFactory.CreateRepository();
                        var taskExecutionInstance = await repo.GetByIdAsync(taskId);
                        var fileName = "x.log";
                        if (taskExecutionInstance == null)
                            fileName = $"{taskId}.log";
                        else
                            fileName = $"{taskExecutionInstance.Name}.log";

                        var memoryStream = new MemoryStream();
                        using var streamWriter = new StreamWriter(memoryStream, leaveOpen: true);
                        int pageIndex = 1;
                        while (true)
                        {
                            var taskLogs = await taskLogRepo.ListAsync(new TaskLogSpecification(taskId, pageIndex, 100));
                            if (taskLogs.Count == 0)
                            {
                                break;
                            }
                            foreach (var taskLog in taskLogs)
                            {
                                pageIndex++;
                                foreach (var logEntry in taskLog.LogEntries)
                                {
                                    streamWriter.WriteLine($"{logEntry.DateTimeUtc.ToString(NodePropertyModel.DateTimeFormatString)} {logEntry.Value}");
                                }
                            }
                            if (taskLogs.Count < 100)
                            {
                                break;
                            }
                        }

                        await streamWriter.FlushAsync();
                        memoryStream.Position = 0;
                        return File(memoryStream, "text/plain", fileName);
                    }
                    else
                    {
                        var taskLog = await taskLogRepo.FirstOrDefaultAsync(new TaskLogSpecification(taskId, queryParameters.PageIndex));
                        if (taskLog == null)
                        {
                            apiResponse.SetResult([]);
                        }
                        else
                        {
                            apiResponse.SetResult(taskLog.LogEntries);
                            apiResponse.SetPageIndex(queryParameters.PageIndex);
                            apiResponse.SetPageSize(taskLog.PageSize);
                        }
                    }


                }
                else
                {
                    apiResponse.SetResult([]);
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

        return Json(apiResponse);
    }
}