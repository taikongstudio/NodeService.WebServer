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

namespace NodeService.WebServer.Services.DataQueue;

public record struct TaskLogQueryServiceParameters
{
    public TaskLogQueryServiceParameters(
        string taskId,
        PaginationQueryParameters parameters)
    {
        TaskExecutionInstanceId = taskId;
        QueryParameters = parameters;
    }

    public string TaskExecutionInstanceId { get; private set; }

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
    readonly BatchQueue<AsyncOperation<TaskLogQueryServiceParameters, TaskLogQueryServiceResult>> _queryBatchQueue;
    readonly ApplicationRepositoryFactory<TaskLogModel> _taskLogRepoFactory;
    readonly ApplicationRepositoryFactory<TaskDefinitionModel> _taskDefinitionRepoFactory;
    readonly ApplicationRepositoryFactory<TaskExecutionInstanceModel> _taskExecutionInstanceRepoFactory;

    public TaskLogQueryService(
        ExceptionCounter exceptionCounter,
        ILogger<TaskLogQueryService> logger,
        ApplicationRepositoryFactory<TaskLogModel> taskLogRepoFactory,
        ApplicationRepositoryFactory<TaskDefinitionModel> taskDefinitionRepoFactory,
        ApplicationRepositoryFactory<TaskExecutionInstanceModel> taskExecutionInstanceRepoFactory,
        BatchQueue<AsyncOperation<TaskLogQueryServiceParameters, TaskLogQueryServiceResult>> queryBatchQueue
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
                foreach (var op in array.Where(static x => x.Kind == AsyncOperationKind.Query).OrderByDescending(static x => x.Priority))
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
                        op.TrySetException(ex);
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
        AsyncOperation<TaskLogQueryServiceParameters, TaskLogQueryServiceResult> op,
        CancellationToken cancellationToken = default)
    {

        try
        {

            var serviceParameters = op.Argument;
            var queryParameters = serviceParameters.QueryParameters;
            string taskId = serviceParameters.TaskExecutionInstanceId;
            var taskExecutionInstance = await taskExecutionInstanceRepo.GetByIdAsync(taskId, cancellationToken);
            if (taskExecutionInstance == null)
            {
                op.TrySetException(new Exception("Could not found task execution instance"));
                return;
            }
            var logFileName = $"{taskExecutionInstance.Name}.log";

            var taskInfoLog = await taskLogRepo.GetByIdAsync(serviceParameters.TaskExecutionInstanceId, cancellationToken);
            if (taskInfoLog == null)
            {
                op.TrySetException(new Exception("Could not found task log"));
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
                    op.TrySetResult(serviceResult);
                    var pageIndex = 1;
                    while (!op.CancellationToken.IsCancellationRequested)
                    {
                        var result = await taskLogRepo.ListAsync(new TaskLogSelectSpecification<TaskLogModel>(taskId, pageIndex, 10), cancellationToken);
                        if (result.Count == 0) break;
                        pageIndex += 10;
                        foreach (var taskLog in result)
                        {
                            if (taskLog == null)
                            {
                                continue;
                            }
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
                    var taskLog = await taskLogRepo.FirstOrDefaultAsync(new TaskLogSelectSpecification<TaskLogModel>(taskId, serviceParameters.QueryParameters.PageIndex), cancellationToken);


                    if (taskLog == null)
                    {
                        op.TrySetResult(serviceResult);
                    }
                    else
                    {
                        serviceResult.TotalCount = taskInfoLog.ActualSize;
                        serviceResult.PageIndex = taskLog.PageIndex;
                        serviceResult.PageSize = taskLog.ActualSize;
                        op.TrySetResult(serviceResult);
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
            op.TrySetException(ex);
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
        finally
        {
        }
    }
}