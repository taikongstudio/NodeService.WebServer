using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.NodeSessions;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.NodeSessions;

namespace NodeService.WebServer.Services.Tasks;

public class TaskCancellationQueueService : BackgroundService
{
    readonly ILogger<TaskCancellationQueueService> _logger;
    readonly ExceptionCounter _exceptionCounter;
    readonly INodeSessionService _nodeSessionService;
    readonly BatchQueue<TaskCancellationParameters> _taskCancellationBatchQueue;
    readonly IAsyncQueue<TaskExecutionReport> _taskExecutionReportBatchQueue;
    readonly ApplicationRepositoryFactory<TaskExecutionInstanceModel> _taskExecutionInstanceRepoFactory;
    readonly IMemoryCache _memoryCache;

    public TaskCancellationQueueService(
        ILogger<TaskCancellationQueueService> logger,
        ExceptionCounter exceptionCounter,
        INodeSessionService nodeSessionService,
        BatchQueue<TaskCancellationParameters> taskCancellationBatchQueue,
        IAsyncQueue<TaskExecutionReport> taskExecutionReportBatchQueue,
        ApplicationRepositoryFactory<TaskExecutionInstanceModel> taskExecutionInstanceRepoFactory,
        IMemoryCache memoryCache)
    {
        _logger = logger;
        _exceptionCounter = exceptionCounter;
        _nodeSessionService = nodeSessionService;
        _taskCancellationBatchQueue = taskCancellationBatchQueue;
        _taskExecutionReportBatchQueue = taskExecutionReportBatchQueue;
        _taskExecutionInstanceRepoFactory = taskExecutionInstanceRepoFactory;
        _memoryCache = memoryCache;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {

        await foreach (var array in _taskCancellationBatchQueue.ReceiveAllAsync(cancellationToken))
        {
            try
            {
                if (array == null)
                {
                    continue;
                }
                await using var taskExecutionInstanceRepo = await _taskExecutionInstanceRepoFactory.CreateRepositoryAsync();
                foreach (var taskCancellationParameters in array)
                {
                    if (taskCancellationParameters == null)
                    {
                        continue;
                    }
                    var taskExecutionInstance = await taskExecutionInstanceRepo.GetByIdAsync(
                                                taskCancellationParameters.TaskExeuctionInstanceId,
                                                cancellationToken);
                    if (taskExecutionInstance == null) continue;
                    if (taskExecutionInstance.Status is not TaskExecutionStatus.PenddingCancel and >= TaskExecutionStatus.Cancelled) continue;
                    if (taskExecutionInstance.Status >= TaskExecutionStatus.Started)
                    {
                        var nodeSessions = _nodeSessionService.EnumNodeSessions(new NodeId(taskExecutionInstance.NodeInfoId), NodeStatus.Online).ToArray();
                        if (nodeSessions.Length != 0)
                        {
                            var req = taskExecutionInstance.ToCancelEvent(taskCancellationParameters);
                            _memoryCache.Set($"{nameof(TaskCancellationQueueService)}:{taskExecutionInstance.Id}", taskCancellationParameters, TimeSpan.FromHours(1));
                            foreach (var nodeSessionId in nodeSessions)
                            {
                                await _nodeSessionService.PostTaskExecutionEventAsync(
                                    nodeSessionId,
                                    req,
                                    null,
                                    cancellationToken);
                            }

                        }
                        else
                        {
                            await _taskExecutionReportBatchQueue.EnqueueAsync(
                                new TaskExecutionReport()
                                {
                                    Status = TaskExecutionStatus.Cancelled,
                                    Id = taskExecutionInstance.Id,
                                    Message = $"Cancelled by {taskCancellationParameters.Source} on {taskCancellationParameters.Context}"
                                },
                            cancellationToken);
                        }
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

}