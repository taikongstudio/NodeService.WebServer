using System.IO.Pipelines;
using System.Text;
using System.Threading.Channels;
using Microsoft.AspNetCore.WebUtilities;
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
    readonly BatchQueue<BatchQueueOperation<TaskLogQueryServiceParameters, TaskLogQueryServiceResult>> _logQueryBatchQueue;
    readonly BatchQueue<TaskActivateServiceParameters> _taskActivateServiceParametersBatchQueue;
    readonly ApplicationRepositoryFactory<TaskExecutionInstanceModel> _taskExecutionInstanceRepositoryFactory;
    readonly ApplicationRepositoryFactory<TaskActivationRecordModel> _taskActivationRepositoryFactory;
    readonly ApplicationRepositoryFactory<TaskFlowTemplateModel> _taskDiagramTemplateFactory;
    readonly ApplicationRepositoryFactory<NodeInfoModel> _nodeInfoRepoFactory;
    readonly ApplicationRepositoryFactory<TaskLogModel> _taskLogRepoFactory;

    public TasksController(
        ILogger<NodesController> logger,
        IServiceProvider serviceProvider,
        IMemoryCache memoryCache,
        ExceptionCounter exceptionCounter,
        INodeSessionService nodeSessionService,
        ApplicationRepositoryFactory<TaskExecutionInstanceModel> taskInstanceRepositoryFactory,
        ApplicationRepositoryFactory<TaskActivationRecordModel> taskActivationRepositoryFactory,
        ApplicationRepositoryFactory<TaskFlowTemplateModel> taskDiagramTemplateFactory,
        ApplicationRepositoryFactory<TaskLogModel> taskLogRepoFactory,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepoFactory,
        BatchQueue<TaskCancellationParameters> taskCancellationAsyncQueue,
        BatchQueue<BatchQueueOperation<TaskLogQueryServiceParameters, TaskLogQueryServiceResult>> logQueryBatchQueue,
        BatchQueue<TaskActivateServiceParameters> taskActivateServiceParametersBatchQueue
        )
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _nodeSessionService = nodeSessionService;
        _memoryCache = memoryCache;
        _exceptionCounter = exceptionCounter;
        _taskExecutionInstanceRepositoryFactory = taskInstanceRepositoryFactory;
        _taskActivationRepositoryFactory = taskActivationRepositoryFactory;
        _taskDiagramTemplateFactory = taskDiagramTemplateFactory;
        _nodeInfoRepoFactory = nodeInfoRepoFactory;
        _taskLogRepoFactory = taskLogRepoFactory;
        _taskCancellationAsyncQueue = taskCancellationAsyncQueue;
        _logQueryBatchQueue = logQueryBatchQueue;
        _taskActivateServiceParametersBatchQueue = taskActivateServiceParametersBatchQueue;
    }

    [HttpGet("/api/Tasks/Instances/List")]
    public async Task<PaginationResponse<TaskExecutionInstanceModel>> QueryTaskExecutionInstanceListAsync(
        [FromQuery] QueryTaskExecutionInstanceListParameters queryParameters,
        CancellationToken cancellationToken = default
    )
    {
        var apiResponse = new PaginationResponse<TaskExecutionInstanceModel>();
        try
        {
            using var taskExecutionInstanceRepo = _taskExecutionInstanceRepositoryFactory.CreateRepository();
            var queryResult = await taskExecutionInstanceRepo.PaginationQueryAsync(new TaskExecutionInstanceListSpecification(
                    queryParameters.Keywords,
                    queryParameters.Status,
                    queryParameters.NodeIdList,
                    queryParameters.TaskDefinitionIdList,
                    queryParameters.TaskExecutionInstanceIdList,
                    queryParameters.FireInstanceIdList,
                    queryParameters.BeginDateTime,
                    queryParameters.EndDateTime,
                    queryParameters.SortDescriptions,
                    queryParameters.IncludeNodeInfo),
                queryParameters.PageSize,
                queryParameters.PageIndex
            );
            if (queryParameters.IncludeNodeInfo)
            {
                using var nodeInfoRepo = _nodeInfoRepoFactory.CreateRepository();
                var idFilterList = queryResult.Items.Select(static x => x.NodeInfoId);
                var nodeInfoList = await nodeInfoRepo.ListAsync(
                    new NodeInfoSpecification(DataFilterCollection<string>.Includes(idFilterList)),
                    cancellationToken);
                foreach (var nodeInfo in nodeInfoList)
                {
                    foreach (var taskExecutionInstance in queryResult.Items)
                    {
                        if (taskExecutionInstance.NodeInfoId == nodeInfo.Id)
                        {
                            taskExecutionInstance.NodeInfo = nodeInfo;
                            break;
                        }
                    }
                }
            }

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

    [HttpGet("/api/Tasks/ActivationRecords/List")]
    public async Task<PaginationResponse<TaskActivationRecordModel>> QueryTaskExecutionInstanceGroupListAsync(
    [FromQuery] QueryTaskExecutionInstanceListParameters queryParameters
)
    {
        var apiResponse = new PaginationResponse<TaskActivationRecordModel>();
        try
        {
            using var repo = _taskActivationRepositoryFactory.CreateRepository();
            var queryResult = await repo.PaginationQueryAsync(new TaskActivationRecordSelectSpecification(
                    queryParameters.Keywords,
                    queryParameters.Status,
                    queryParameters.BeginDateTime,
                    queryParameters.EndDateTime,
                    DataFilterCollection<string>.Includes(queryParameters.FireInstanceIdList),
                    DataFilterCollection<string>.Includes (queryParameters.TaskDefinitionIdList),
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

    [HttpGet("/api/Tasks/Instances/{id}/Retry")]
    public async Task<ApiResponse<TaskExecutionInstanceModel>> RetryTaskAsync(string id, CancellationToken cancellationToken = default)
    {
        var apiResponse = new ApiResponse<TaskExecutionInstanceModel>();
        try
        {
            var retryTaskParameters = new RetryTaskParameters(id);
            await _taskActivateServiceParametersBatchQueue.SendAsync(
                new TaskActivateServiceParameters(retryTaskParameters),
                cancellationToken);
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
                taskCancellationParameters.TaskExeuctionInstanceId,
                taskCancellationParameters.Source,
                HttpContext.Connection.RemoteIpAddress.ToString()));
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
        CancellationToken cancellationToken = default)
    {
        var apiResponse = new PaginationResponse<LogEntry>();
        try
        {
            var serviceParameters = new TaskLogQueryServiceParameters(
                                        taskId,
                                        queryParameters);
            var op = new BatchQueueOperation<TaskLogQueryServiceParameters, TaskLogQueryServiceResult>(
                serviceParameters,
                BatchQueueOperationKind.Query,
                BatchQueueOperationPriority.Lowest,
                cancellationToken);

            await _logQueryBatchQueue.SendAsync(op, cancellationToken);

            var result = await op.WaitAsync(cancellationToken);
            HttpContext.Response.Headers.Append(nameof(PaginationResponse<int>.TotalCount), result.TotalCount.ToString());
            HttpContext.Response.Headers.Append(nameof(PaginationResponse<int>.PageIndex), result.PageIndex.ToString());
            HttpContext.Response.Headers.Append(nameof(PaginationResponse<int>.PageSize), result.PageSize.ToString());
            return File(result.Pipe.Reader.AsStream(), "text/plain", result.LogFileName);
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