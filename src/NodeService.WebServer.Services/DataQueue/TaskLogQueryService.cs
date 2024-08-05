using MongoDB.Driver.GridFS;
using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Logging;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using System.IO.Pipelines;

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

    public long TotalLengthInBytes { get; set; }

    public long CurrentPagePosition { get; set; }
}

public class TaskLogQueryService 
{
    class TaskLogReadPositionCache
    {
        public int PageIndex { get; set; }

        public int PageSize { get; set; }

        public long Position { get; set; }
    }

    readonly ExceptionCounter _exceptionCounter;
    readonly ILogger<TaskLogQueryService> _logger;
    readonly ApplicationRepositoryFactory<TaskLogModel> _taskLogRepoFactory;
    readonly ApplicationRepositoryFactory<TaskDefinitionModel> _taskDefinitionRepoFactory;
    readonly ApplicationRepositoryFactory<TaskExecutionInstanceModel> _taskExecutionInstanceRepoFactory;
    readonly MongoGridFS _mongoGridFS;
    private readonly IMemoryCache _memoryCache;

    public TaskLogQueryService(
        ExceptionCounter exceptionCounter,
        ILogger<TaskLogQueryService> logger,
        MongoGridFS mongoGridFS,
        ApplicationRepositoryFactory<TaskLogModel> taskLogRepoFactory,
        ApplicationRepositoryFactory<TaskDefinitionModel> taskDefinitionRepoFactory,
        ApplicationRepositoryFactory<TaskExecutionInstanceModel> taskExecutionInstanceRepoFactory,
       IMemoryCache memoryCache
    )
    {
        _exceptionCounter = exceptionCounter;
        _logger = logger;
        _taskLogRepoFactory = taskLogRepoFactory;
        _taskDefinitionRepoFactory = taskDefinitionRepoFactory;
        _taskExecutionInstanceRepoFactory = taskExecutionInstanceRepoFactory;
        _mongoGridFS = mongoGridFS;
        _memoryCache = memoryCache;
    }

    public async ValueTask<bool> DeleteAsync(
        string taskExecutionInstanceId,
    CancellationToken cancellationToken = default)
    {
        try
        {
            var taskInfoLog = await GetTaskInfoLogAsync(taskExecutionInstanceId, cancellationToken);
            if (taskInfoLog == null)
            {
                return false;
            }
            if (taskInfoLog.Name.StartsWith("mongodb://"))
            {
                var taskLogFileId = taskInfoLog.Name["mongodb://".Length..];
                _mongoGridFS.DeleteById(taskLogFileId);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
            _exceptionCounter.AddOrUpdate(ex, taskExecutionInstanceId);
        }
        return false;
    }


            public async ValueTask<TaskLogQueryServiceResult> QueryAsync(
      TaskLogQueryServiceParameters serviceParameters,
        CancellationToken cancellationToken = default)
    {

        var queryParameters = serviceParameters.QueryParameters;
        string taskId = serviceParameters.TaskExecutionInstanceId;
        var taskExecutionInstance = await GetTaskExecutionInstanceAsync(taskId, cancellationToken);
        if (taskExecutionInstance == null)
        {
            throw new Exception("Could not found task execution instance");
        }
        var logFileName = $"{taskExecutionInstance.Name}.log";
        var taskInfoLog = await GetTaskInfoLogAsync(serviceParameters.TaskExecutionInstanceId, cancellationToken);
        if (taskInfoLog == null)
        {
            throw new Exception("Could not found task log");
        }
        var pipe = new Pipe();
        var streamWriter = new StreamWriter(pipe.Writer.AsStream());
        var serviceResult = new TaskLogQueryServiceResult(logFileName, pipe);
        if (taskInfoLog.Name.StartsWith("mongodb://"))
        {
            var taskLogFileId = taskInfoLog.Name["mongodb://".Length..];
            var fs = _mongoGridFS.OpenRead(taskLogFileId);
            serviceResult.TotalCount = taskInfoLog.ActualSize;


            if (queryParameters.PageIndex == 0)
            {
                _ = Task.Run(async () =>
                {

                    try
                    {

                        using var streamReder = new StreamReader(fs);
                        while (!cancellationToken.IsCancellationRequested && !streamReder.EndOfStream)
                        {
                            var str = await streamReder.ReadLineAsync(cancellationToken);
                            if (str == null)
                            {
                                continue;
                            }
                            var taskLogEntry = JsonSerializer.Deserialize<LogEntry>(str);
                            if (taskLogEntry==null)
                            {
                                continue;
                            }
                            await streamWriter.WriteLineAsync($"{taskLogEntry.DateTimeUtc.ToString(NodePropertyModel.DateTimeFormatString)} {taskLogEntry.Value}");
                        }
                        await streamWriter.FlushAsync();

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
                foreach (var item in taskInfoLog.Value.LogEntries)
                {
                    if (queryParameters.PageIndex == item.Index + 1)
                    {
                        serviceResult.PageIndex = item.Index + 1;
                        serviceResult.PageSize = item.Status;
                        if (queryParameters.PageIndex == 1)
                        {
                            fs.Position = 0;
                        }
                        else
                        {
                            fs.Position = item.Type;
                        }
                        break;
                    }
                }


                _ = Task.Run(async () =>
                {

                    try
                    {

                        using var streamReder = new StreamReader(fs);
                        while (!cancellationToken.IsCancellationRequested && !streamReder.EndOfStream)
                        {
                            for (int i = 0; i < serviceResult.PageSize; i++)
                            {
                                var str = await streamReder.ReadLineAsync(cancellationToken);
                                if (str == null)
                                {
                                    break;
                                }
                                var taskLogEntry = JsonSerializer.Deserialize<LogEntry>(str);
                                if (taskLogEntry == null)
                                {
                                    break;
                                }
                                await streamWriter.WriteLineAsync($"{taskLogEntry.DateTimeUtc.ToString(NodePropertyModel.DateTimeFormatString)} {taskLogEntry.Value}");
                            }
                            break;
                        }
                        await streamWriter.FlushAsync();

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

        }
        else
        {

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
                            await using var taskLogRepo = await _taskLogRepoFactory.CreateRepositoryAsync();
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
                await using var taskLogRepo = await _taskLogRepoFactory.CreateRepositoryAsync();
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
        }

        return serviceResult;
    }

    async ValueTask<TaskLogModel?> GetTaskInfoLogAsync(string taskExecutionInstanceId, CancellationToken cancellationToken = default)
    {
        await using var taskLogRepo = await _taskLogRepoFactory.CreateRepositoryAsync(cancellationToken);
        var taskInfoLog = await taskLogRepo.GetByIdAsync(taskExecutionInstanceId, cancellationToken);
        return taskInfoLog;
    }

    async ValueTask<TaskExecutionInstanceModel?> GetTaskExecutionInstanceAsync(string taskExecutionInstanceId, CancellationToken cancellationToken = default)
    {
        await using var taskExecutionInstanceRepo = await _taskExecutionInstanceRepoFactory.CreateRepositoryAsync();
        var taskExecutionInstance = await taskExecutionInstanceRepo.GetByIdAsync(taskExecutionInstanceId, cancellationToken);
        return taskExecutionInstance;
    }
}