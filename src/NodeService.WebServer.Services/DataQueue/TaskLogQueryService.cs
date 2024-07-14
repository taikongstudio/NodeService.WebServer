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

public record struct TaskLogQueryServiceResult
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

public class TaskLogQueryService 
{
    readonly ExceptionCounter _exceptionCounter;
    readonly ILogger<TaskLogQueryService> _logger;
    readonly ApplicationRepositoryFactory<TaskLogModel> _taskLogRepoFactory;
    readonly ApplicationRepositoryFactory<TaskDefinitionModel> _taskDefinitionRepoFactory;
    readonly ApplicationRepositoryFactory<TaskExecutionInstanceModel> _taskExecutionInstanceRepoFactory;

    public TaskLogQueryService(
        ExceptionCounter exceptionCounter,
        ILogger<TaskLogQueryService> logger,
        ApplicationRepositoryFactory<TaskLogModel> taskLogRepoFactory,
        ApplicationRepositoryFactory<TaskDefinitionModel> taskDefinitionRepoFactory,
        ApplicationRepositoryFactory<TaskExecutionInstanceModel> taskExecutionInstanceRepoFactory
    )
    {
        _exceptionCounter = exceptionCounter;
        _logger = logger;
        _taskLogRepoFactory = taskLogRepoFactory;
        _taskDefinitionRepoFactory = taskDefinitionRepoFactory;
        _taskExecutionInstanceRepoFactory = taskExecutionInstanceRepoFactory;
    }


    public async ValueTask<TaskLogQueryServiceResult> QueryAsync(
      TaskLogQueryServiceParameters serviceParameters,
        CancellationToken cancellationToken = default)
    {
        await using var taskDefinitionRepo = await _taskDefinitionRepoFactory.CreateRepositoryAsync();
        await using var taskExecutionInstanceRepo = await _taskExecutionInstanceRepoFactory.CreateRepositoryAsync();
        await using var taskLogRepo = await _taskLogRepoFactory.CreateRepositoryAsync();
        var queryParameters = serviceParameters.QueryParameters;
        string taskId = serviceParameters.TaskExecutionInstanceId;
        var taskExecutionInstance = await taskExecutionInstanceRepo.GetByIdAsync(taskId, cancellationToken);
        if (taskExecutionInstance == null)
        {
            throw new Exception("Could not found task execution instance");
        }
        var logFileName = $"{taskExecutionInstance.Name}.log";

        var taskInfoLog = await taskLogRepo.GetByIdAsync(serviceParameters.TaskExecutionInstanceId, cancellationToken);
        if (taskInfoLog == null)
        {
            throw new Exception("Could not found task log");
        }
        var pipe = new Pipe();
        var streamWriter = new StreamWriter(pipe.Writer.AsStream());
        var serviceResult = new TaskLogQueryServiceResult(logFileName, pipe);
        var totalLogCount = taskInfoLog.ActualSize;
        if (serviceParameters.QueryParameters.PageIndex == 0)
        {
            serviceResult.TotalCount = taskInfoLog.ActualSize;
            serviceResult.PageIndex = taskInfoLog.PageIndex;
            serviceResult.PageSize = taskInfoLog.PageSize;

            _ = Task.Run(async () =>
            {
                try
                {
                    var pageIndex = 1;
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var result = await taskLogRepo.ListAsync(
                            new TaskLogSelectSpecification<TaskLogModel>(taskId, pageIndex, 10),
                            cancellationToken);
                        if (result.Count == 0) break;
                        pageIndex += 10;
                        foreach (var taskLog in result)
                        {
                            if (taskLog == null)
                            {
                                continue;
                            }
                            foreach (var taskLogEntry in taskLog.LogEntries)
                            {
                                streamWriter.WriteLine($"{taskLogEntry.DateTimeUtc.ToString(NodePropertyModel.DateTimeFormatString)} {taskLogEntry.Value}");
                            }
                        }
                        if (result.Count < 10) break;
                    }
                }
                catch (Exception ex)
                {
                    _exceptionCounter.AddOrUpdate(ex);
                    _logger.LogError(ex.ToString());
                }
                finally
                {
                    await streamWriter.DisposeAsync();
                }

            }, cancellationToken);

        }
        else
        {
            var taskLog = await taskLogRepo.FirstOrDefaultAsync(new TaskLogSelectSpecification<TaskLogModel>(taskId, serviceParameters.QueryParameters.PageIndex), cancellationToken);
            if (taskLog != null)
            {
                serviceResult.TotalCount = taskInfoLog.ActualSize;
                serviceResult.PageIndex = taskLog.PageIndex;
                serviceResult.PageSize = taskLog.ActualSize;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        foreach (var taskLogEntry in taskLog.Value.LogEntries)
                        {
                            streamWriter.WriteLine($"{taskLogEntry.DateTimeUtc.ToString(NodePropertyModel.DateTimeFormatString)} {taskLogEntry.Value}");
                        }

                    }
                    catch (Exception ex)
                    {
                        _exceptionCounter.AddOrUpdate(ex);
                        _logger.LogError(ex.ToString());
                    }
                    finally
                    {
                        await streamWriter.DisposeAsync();
                    }
                });
        }



        }
        return serviceResult;
    }
}