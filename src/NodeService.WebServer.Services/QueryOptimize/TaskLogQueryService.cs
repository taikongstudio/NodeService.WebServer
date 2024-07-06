using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Logging;
using NodeService.Infrastructure.Models;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using OneOf;
using System.IO.Pipelines;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.QueryOptimize;

public record struct TaskLogQueryServiceParameters
{
    public TaskLogQueryServiceParameters(
        string taskId,
        PaginationQueryParameters parameters)
    {
        TaskId = taskId;
        QueryParameters = parameters;
    }

    public string TaskId { get; private set; }

    public PaginationQueryParameters QueryParameters { get; private set; }

}

public record class TaskLogQueryServiceResult
{

    public TaskLogQueryServiceResult(string logFileName, Pipe pipe)
    {
        LogFileName = logFileName;
        Pipe = pipe;
    }

    public string LogFileName { get; private set; }

    public Pipe Pipe { get; private set; }

    public int TotalCount { get; set; }

    public int PageIndex { get; set; }

    public int PageSize { get; set; }
}

public class TaskLogQueryService : BackgroundService
{
    readonly ExceptionCounter _exceptionCounter;
    readonly ILogger<TaskLogQueryService> _logger;
    readonly BatchQueue<BatchQueueOperation<TaskLogQueryServiceParameters, TaskLogQueryServiceResult>> _queryBatchQueue;
    readonly ApplicationRepositoryFactory<TaskLogModel> _taskLogRepoFactory;
    readonly ApplicationRepositoryFactory<TaskDefinitionModel> _taskDefinitionRepoFactory;
    readonly ApplicationRepositoryFactory<TaskExecutionInstanceModel> _taskExecutionInstanceRepoFactory;

    public TaskLogQueryService(
        ExceptionCounter exceptionCounter,
        ILogger<TaskLogQueryService> logger,
        ApplicationRepositoryFactory<TaskLogModel> taskLogRepoFactory,
        ApplicationRepositoryFactory<TaskDefinitionModel> taskDefinitionRepoFactory,
        ApplicationRepositoryFactory<TaskExecutionInstanceModel> taskExecutionInstanceRepoFactory,
        BatchQueue<BatchQueueOperation<TaskLogQueryServiceParameters, TaskLogQueryServiceResult>> queryBatchQueue
    )
    {
        _exceptionCounter = exceptionCounter;
        _logger = logger;
        _taskLogRepoFactory = taskLogRepoFactory;
        _taskDefinitionRepoFactory = taskDefinitionRepoFactory;
        _taskExecutionInstanceRepoFactory = taskExecutionInstanceRepoFactory;
        _queryBatchQueue = queryBatchQueue;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await ProcessTaskLogQueryAsync(cancellationToken);

    }

    private async Task ProcessTaskLogQueryAsync(CancellationToken cancellationToken = default)
    {
        await foreach (var array in _queryBatchQueue.ReceiveAllAsync(cancellationToken))
        {
            try
            {
                if (array == null)
                {
                    continue;
                }
                using var taskDefinitionRepo = _taskDefinitionRepoFactory.CreateRepository();
                using var taskExecutionInstanceRepo = _taskExecutionInstanceRepoFactory.CreateRepository();
                using var taskLogRepo = _taskLogRepoFactory.CreateRepository();
                foreach (var op in array.Where(static x => x.Kind == BatchQueueOperationKind.Query).OrderByDescending(static x => x.Priority))
                {
                    try
                    {
                        await QueryTaskLogAsync(
                            taskDefinitionRepo,
                            taskLogRepo,
                            taskExecutionInstanceRepo,
                            op,
                            cancellationToken);

                    }
                    catch (Exception ex)
                    {
                        op.SetException(ex);
                        _exceptionCounter.AddOrUpdate(ex);
                        _logger.LogError(ex.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
            finally
            {
            }
        }
    }

    async ValueTask QueryTaskLogAsync(
        IRepository<TaskDefinitionModel> taskDefinitionRepo,
        IRepository<TaskLogModel> taskLogRepo,
        IRepository<TaskExecutionInstanceModel> taskExecutionInstanceRepo,
        BatchQueueOperation<TaskLogQueryServiceParameters, TaskLogQueryServiceResult> op,
        CancellationToken cancellationToken = default)
    {

        try
        {

            var serviceParameters = op.Argument;
            var queryParameters = serviceParameters.QueryParameters;
            string taskId = serviceParameters.TaskId;
            var taskExecutionInstance = await taskExecutionInstanceRepo.GetByIdAsync(taskId, cancellationToken);
            if (taskExecutionInstance == null)
            {
                op.SetException(new Exception("Could not found task execution instance"));
                return;
            }
            var logFileName = $"{taskExecutionInstance.Name}.log";

            var taskInfoLog = await taskLogRepo.FirstOrDefaultAsync(new TaskLogSpecification(serviceParameters.TaskId, 0), cancellationToken);
            if (taskInfoLog == null)
            {
                op.SetException(new Exception("Could not found task log"));
                return;
            }
            else
            {
                var pipe = new Pipe();
                using var streamWriter = new StreamWriter(pipe.Writer.AsStream());
                var serviceResult = new TaskLogQueryServiceResult(logFileName, pipe);
                var totalLogCount = taskInfoLog.ActualSize;
                if (serviceParameters.QueryParameters.PageIndex == 0)
                {
                    serviceResult.TotalCount = taskInfoLog.ActualSize;
                    serviceResult.PageIndex = taskInfoLog.PageIndex;
                    serviceResult.PageSize = taskInfoLog.PageSize;
                    op.SetResult(serviceResult);
                    var pageIndex = 1;
                    while (!op.CancellationToken.IsCancellationRequested)
                    {
                        var result = await taskLogRepo.ListAsync(new TaskLogSpecification(taskId, pageIndex, 10), cancellationToken);
                        if (result.Count == 0) break;
                        pageIndex++;
                        foreach (var taskLog in result)
                        {
                            foreach (var taskLogEntry in taskLog.Value.LogEntries)
                            {
                                streamWriter.WriteLine($"{taskLogEntry.DateTimeUtc.ToString(NodePropertyModel.DateTimeFormatString)} {taskLogEntry.Value}");
                            }
                        }
                        if (result.Count < 10) break;
                    }

                }
                else
                {
                    var taskLog = await taskLogRepo.FirstOrDefaultAsync(new TaskLogSpecification(taskId, serviceParameters.QueryParameters.PageIndex), cancellationToken);


                    if (taskLog == null)
                    {
                        op.SetResult(serviceResult);
                    }
                    else
                    {
                        serviceResult.TotalCount = taskInfoLog.ActualSize;
                        serviceResult.PageIndex = taskLog.PageIndex;
                        serviceResult.PageSize = taskLog.ActualSize;
                        op.SetResult(serviceResult);
                        if (taskLog != null)
                        {
                            foreach (var taskLogEntry in taskLog.Value.LogEntries)
                            {
                                streamWriter.WriteLine($"{taskLogEntry.DateTimeUtc.ToString(NodePropertyModel.DateTimeFormatString)} {taskLogEntry.Value}");
                            }
                        }
                    }


                }

            }

        }
        catch (Exception ex)
        {
            op.SetException(ex);
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
        finally
        {
        }
    }
}