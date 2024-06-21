using System.Text;
using System.Threading.Channels;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Logging;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.QueryOptimize;
using NodeService.WebServer.Services.Tasks;

namespace NodeService.WebServer.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public class TasksController : Controller
{
    readonly ExceptionCounter _exceptionCounter;
    readonly ILogger<NodesController> _logger;
    readonly IMemoryCache _memoryCache;
    readonly INodeSessionService _nodeSessionService;
    readonly IServiceProvider _serviceProvider;
    readonly BatchQueue<TaskCancellationParameters> _taskCancellationAsyncQueue;
    readonly BatchQueue<BatchQueueOperation<TaskLogQueryServiceParameters, TaskLogQueryServiceResult>> _queryBatchQueue;
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
        BatchQueue<TaskCancellationParameters> taskCancellationAsyncQueue,
        BatchQueue<BatchQueueOperation<TaskLogQueryServiceParameters, TaskLogQueryServiceResult>> queryBatchQueue)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _taskInstanceRepositoryFactory = taskInstanceRepositoryFactory;
        _nodeSessionService = nodeSessionService;
        _memoryCache = memoryCache;
        _taskLogRepoFactory = taskLogRepoFactory;
        _exceptionCounter = exceptionCounter;
        _taskCancellationAsyncQueue = taskCancellationAsyncQueue;
        _queryBatchQueue = queryBatchQueue;
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
            await _taskCancellationAsyncQueue.SendAsync(new TaskCancellationParameters(
                taskCancellationParameters.Source,
                HttpContext.Connection.RemoteIpAddress.ToString(),
                taskCancellationParameters.TaskExeuctionInstanceId));
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
        [FromQuery] PaginationQueryParameters queryParameters,
        CancellationToken cancellationToken=default)
    {
        var apiResponse = new PaginationResponse<LogEntry>();
        try
        {
            var serviceParameters = new TaskLogQueryServiceParameters(
                                        taskId,
                                        queryParameters);
            if (queryParameters.PageIndex == 0)
            {
                var op = new BatchQueueOperation<TaskLogQueryServiceParameters, TaskLogQueryServiceResult>(
                    serviceParameters,
                    BatchQueueOperationKind.Query,
                    BatchQueueOperationPriority.Lowest);
                
                await _queryBatchQueue.SendAsync(op, cancellationToken);

                var result =await op.WaitAsync(cancellationToken);

                var memoryStream = new MemoryStream();

                using var streamWriter = new StreamWriter(memoryStream, leaveOpen: true);
                await foreach (var listQueryResult in result.LogChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    foreach (var taskLogEntry in listQueryResult.Items)
                    {
                        streamWriter.WriteLine($"{taskLogEntry.DateTimeUtc.ToString(NodePropertyModel.DateTimeFormatString)} {taskLogEntry.Value}");
                    }
        
                }
                await streamWriter.FlushAsync(cancellationToken);
                memoryStream.Position = 0;
                return File(memoryStream, "text/plain", result.LogFileName);
            }
            else
            {
                var op = new BatchQueueOperation<TaskLogQueryServiceParameters, TaskLogQueryServiceResult>(
                        serviceParameters,
                        BatchQueueOperationKind.Query,
                        BatchQueueOperationPriority.Lowest);

                await _queryBatchQueue.SendAsync(op, cancellationToken);

                var result = await op.WaitAsync(cancellationToken);

                var listQueryResults = await result.LogChannel.Reader.ReadAllAsync(cancellationToken).ToArrayAsync(cancellationToken);
                if (listQueryResults == null||listQueryResults.Length==0)
                {
                    apiResponse.SetResult([]);
                }
                else
                {
                    var listQueryResult = listQueryResults.FirstOrDefault();
                    apiResponse.SetResult(listQueryResult.Items);
                    apiResponse.SetPageIndex(listQueryResult.PageIndex);
                    apiResponse.SetPageSize(listQueryResult.PageSize);
                    apiResponse.SetTotalCount(listQueryResult.TotalCount);
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