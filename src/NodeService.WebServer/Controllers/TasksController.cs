﻿using CommandLine;
using NodeService.Infrastructure.Concurrent;
using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.Logging;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.DataServices;
using NodeService.WebServer.Services.Tasks;
using Quartz.Impl.Matchers;

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
    readonly BatchQueue<AsyncOperation<TaskLogQueryServiceParameters, TaskLogQueryServiceResult>> _logQueryBatchQueue;
    readonly BatchQueue<TaskActivateServiceParameters> _taskActivateServiceParametersBatchQueue;
    readonly ObjectCache _objectCache;
    private readonly ApplicationRepositoryFactory<TaskProgressInfoModel> _taskProgressInfoRepoFactory;
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
        ObjectCache objectCache,
        ApplicationRepositoryFactory<TaskExecutionInstanceModel> taskInstanceRepositoryFactory,
        ApplicationRepositoryFactory<TaskActivationRecordModel> taskActivationRepositoryFactory,
        ApplicationRepositoryFactory<TaskFlowTemplateModel> taskDiagramTemplateFactory,
        ApplicationRepositoryFactory<TaskLogModel> taskLogRepoFactory,
        ApplicationRepositoryFactory<NodeInfoModel> nodeInfoRepoFactory,
        ApplicationRepositoryFactory<TaskProgressInfoModel> taskProgressInfoRepoFactory,
        BatchQueue<TaskCancellationParameters> taskCancellationAsyncQueue,
        BatchQueue<AsyncOperation<TaskLogQueryServiceParameters, TaskLogQueryServiceResult>> logQueryBatchQueue,
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
        _objectCache = objectCache;
        _taskProgressInfoRepoFactory = taskProgressInfoRepoFactory;
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
            await using var taskExecutionInstanceRepo = await _taskExecutionInstanceRepositoryFactory.CreateRepositoryAsync();
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
            if (queryParameters.IncludeTaskProgressInfo)
            {
                await using var taskProgressInfoRepo = await _taskProgressInfoRepoFactory.CreateRepositoryAsync(cancellationToken);
                foreach (var taskExecutionInstance in queryResult.Items)
                {
                    var taskProgressInfo = await taskProgressInfoRepo.GetByIdAsync(taskExecutionInstance.Id, cancellationToken);
                    if (taskProgressInfo != null)
                    {
                        taskExecutionInstance.TaskProgressInfo = taskProgressInfo.Value;
                    }
                }
            }
            if (queryParameters.IncludeNodeInfo)
            {
                await using var nodeInfoRepo = await _nodeInfoRepoFactory.CreateRepositoryAsync();
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
            await using var repo = await _taskActivationRepositoryFactory.CreateRepositoryAsync();
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
            var taskLogQueryService = _serviceProvider.GetService<TaskLogQueryService>();
            var serviceParameters = new TaskLogQueryServiceParameters(
                            taskId,
                            queryParameters);
            var result = await taskLogQueryService.QueryAsync(serviceParameters);
            HttpContext.Response.Headers.Append(nameof(PaginationResponse<int>.TotalCount), result.TotalCount.ToString());
            HttpContext.Response.Headers.Append(nameof(PaginationResponse<int>.PageIndex), result.PageIndex.ToString());
            HttpContext.Response.Headers.Append(nameof(PaginationResponse<int>.PageSize), result.PageSize.ToString());
            return File(result.Pipe.Reader.AsStream(true), "text/plain", result.LogFileName);
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

    [HttpGet("/api/Tasks/JobDetails")]
    public async Task<PaginationResponse<JobDetail>> QueryJobDetailAsync(
    CancellationToken cancellationToken = default)
    {
        var apiResponse = new PaginationResponse<JobDetail>();
        try
        {
            var schedulerFactory = _serviceProvider.GetService<ISchedulerFactory>();
            var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
            var keys = await scheduler.GetJobKeys(GroupMatcher<Quartz.JobKey>.AnyGroup(), cancellationToken);
            var list = new List<JobDetail>();
            foreach (var jobKey in keys)
            {
                var jobDetailObject = await scheduler.GetJobDetail(jobKey, cancellationToken);
                var jobDetail = jobDetailObject.ToJobDetail();
                var triggers = await scheduler.GetTriggersOfJob(jobKey, cancellationToken);
                foreach (var trigger in triggers)
                {
                    var triggerInfo = await trigger.ToTriggerInfoAsync(scheduler, cancellationToken);
                    jobDetail.Triggers.Add(triggerInfo);
                }
                list.Add(jobDetail);
            }
            apiResponse.SetResult(new ListQueryResult<JobDetail>(list.Count, 1, list.Count, list));
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
}