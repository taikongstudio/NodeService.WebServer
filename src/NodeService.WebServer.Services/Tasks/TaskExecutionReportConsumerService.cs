using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Data;
using NodeService.Infrastructure.DataModels;
using NodeService.Infrastructure.Logging;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.NodeFileSystem;
using NodeService.WebServer.Services.NodeSessions;
using NodeService.WebServer.Services.TaskSchedule;
using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;

namespace NodeService.WebServer.Services.Tasks;

public partial class TaskExecutionReportConsumerService : BackgroundService
{

    readonly ExceptionCounter _exceptionCounter;
    readonly TaskFlowExecutor _taskFlowExecutor;
    readonly ConfigurationQueryService _configurationQueryService;
    readonly ITaskPenddingContextManager _taskPenddingContextManager;
    private readonly IAsyncQueue<KafkaDelayMessage> _delayMessageQueue;
    private readonly IDelayMessageBroadcast _delayMessageBroadcast;
    readonly ILogger<TaskExecutionReportConsumerService> _logger;
    readonly IMemoryCache _memoryCache;

    readonly ApplicationRepositoryFactory<TaskExecutionInstanceModel> _taskExecutionInstanceRepoFactory;
    readonly ApplicationRepositoryFactory<TaskActivationRecordModel> _taskActivationRecordRepoFactory;
    readonly ApplicationRepositoryFactory<TaskFlowExecutionInstanceModel> _taskFlowExecutionInstanceRepoFactory;
    readonly ApplicationRepositoryFactory<TaskFlowTemplateModel> _taskFlowTemplateRepoFactory;
    readonly BatchQueue<TaskLogUnit> _taskLogUnitBatchQueue;
    readonly BatchQueue<TaskActivateServiceParameters> _taskActivateQueue;
    readonly BatchQueue<TaskCancellationParameters> _taskCancellationBatchQueue;
    readonly BatchQueue<TaskExecutionReportMessage> _taskExecutionReportBatchQueue;
    readonly JobScheduler _jobScheduler;
    readonly WebServerCounter _webServerCounter;
    readonly TaskActivationRecordExecutor _taskActivationRecordExecutor;
    private readonly IServiceProvider _serviceProvider;
    public  const string SubType_ExecutionTimeLimit = "ExecutionTimeLimit";
    public const string SubType_Retry = "Retry";

    public TaskExecutionReportConsumerService(
        ApplicationRepositoryFactory<TaskExecutionInstanceModel> taskExecutionInstanceRepositoryFactory,
        ApplicationRepositoryFactory<TaskFlowExecutionInstanceModel> taskFlowExecutionInstanceRepoFactory,
        ApplicationRepositoryFactory<TaskFlowTemplateModel> taskFlowTemplateRepoFactory,
        ApplicationRepositoryFactory<TaskActivationRecordModel> taskActivationRecordRepoFactory,
        BatchQueue<TaskExecutionReportMessage> taskExecutionReportBatchQueue,
        BatchQueue<TaskCancellationParameters> taskCancellationBatchQueue,
        BatchQueue<TaskActivateServiceParameters> taskScheduleQueue,
        ILogger<TaskExecutionReportConsumerService> logger,
        ConfigurationQueryService configurationQueryService,
        JobScheduler taskScheduler,
        IMemoryCache memoryCache,
        WebServerCounter webServerCounter,
        ExceptionCounter exceptionCounter,
        TaskFlowExecutor taskFlowExecutor,
        ITaskPenddingContextManager taskPenddingContextManager,
        IAsyncQueue<KafkaDelayMessage> delayMessageQueue,
        IDelayMessageBroadcast delayMessageBroadcast,
        TaskActivationRecordExecutor taskActivationRecordExecutor,
        IServiceProvider serviceProvider
    )
    {
        _logger = logger;
        _taskExecutionInstanceRepoFactory = taskExecutionInstanceRepositoryFactory;
        _taskActivationRecordRepoFactory = taskActivationRecordRepoFactory;
        _taskFlowExecutionInstanceRepoFactory = taskFlowExecutionInstanceRepoFactory;
        _taskFlowTemplateRepoFactory = taskFlowTemplateRepoFactory;
        _taskExecutionReportBatchQueue = taskExecutionReportBatchQueue;
        _taskActivateQueue = taskScheduleQueue;
        _taskCancellationBatchQueue = taskCancellationBatchQueue;
        _jobScheduler = taskScheduler;
        _memoryCache = memoryCache;
        _webServerCounter = webServerCounter;
        _exceptionCounter = exceptionCounter;
        _taskFlowExecutor = taskFlowExecutor;
        _configurationQueryService = configurationQueryService;
        _taskPenddingContextManager = taskPenddingContextManager;
        _delayMessageQueue = delayMessageQueue;
        _delayMessageBroadcast = delayMessageBroadcast;
        _delayMessageBroadcast.AddHandler(nameof(TaskExecutionReportConsumerService), ProcessDelayMessage);
        _taskActivationRecordExecutor = taskActivationRecordExecutor;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(
            ProcessExpiredTasksAsync(cancellationToken),
            ProcessTaskExecutionReportAsync(cancellationToken));
    }

    private async Task ProcessExpiredTasksAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(TimeSpan.FromMinutes(10), cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            await ProcessExpiredTaskExecutionInstanceAsync(cancellationToken);
            await Task.Delay(TimeSpan.FromHours(6), cancellationToken);
        }
    }

    async ValueTask ProcessDelayMessage(KafkaDelayMessage kafkaDelayMessage, CancellationToken cancellationToken = default)
    {
        switch (kafkaDelayMessage.SubType)
        {
            case SubType_ExecutionTimeLimit:
                await ProcessExcutionTimeLimitAsync(kafkaDelayMessage.Id, cancellationToken);
                break;
            case SubType_Retry:
                await ProcessTryAsync(kafkaDelayMessage, cancellationToken);
                break;
            default:
                break;
        }
    }

    private async Task ProcessTryAsync(KafkaDelayMessage kafkaDelayMessage, CancellationToken cancellationToken)
    {
        try
        {
            if (kafkaDelayMessage == null || kafkaDelayMessage.Id == null)
            {
                return;
            }
            if (kafkaDelayMessage.Properties == null
                || !kafkaDelayMessage.Properties.TryGetValue(nameof(FireTaskParameters), out var fireTaskParameterString))
            {
                return;
            }
            var fireTaskParameters = JsonSerializer.Deserialize<FireTaskParameters>(fireTaskParameterString);
            await _taskActivateQueue.SendAsync(new TaskActivateServiceParameters(fireTaskParameters), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
            _exceptionCounter.AddOrUpdate(ex, kafkaDelayMessage.Id);
        }

    }

    private async Task ProcessTaskExecutionReportAsync(CancellationToken cancellationToken = default)
    {
        await foreach (var array in _taskExecutionReportBatchQueue.ReceiveAllAsync(cancellationToken))
        {
            var stopwatch = new Stopwatch();
            _webServerCounter.TaskExecutionReportQueueCount.Value = _taskExecutionReportBatchQueue.QueueCount;
            if (array == null)
            {
                continue;
            }
            try
            {

                stopwatch.Start();
                var reports = array.Select(x => x.GetMessage()).ToArray();
                await ProcessTaskExecutionReportsAsync(reports, cancellationToken);
                stopwatch.Stop();

            }
            catch (Exception ex)
            {
                _exceptionCounter.AddOrUpdate(ex);
                _logger.LogError(ex.ToString());
            }
            finally
            {
                _logger.LogInformation($"process {array.Length} messages,spent: {stopwatch.Elapsed}, QueueCount:{_taskExecutionReportBatchQueue.QueueCount}");
                _webServerCounter.TaskExecutionReportConsumeCount.Value += (uint)array.Length;
                _webServerCounter.TaskExecutionReportTotalTimeSpan.Value += stopwatch.Elapsed;
                stopwatch.Reset();
            }
        }
    }

    static string? GetTaskId(TaskExecutionReport report)
    {
        if (report == null) return null;
        if (report.Properties.TryGetValue("TaskId", out var id)) return id;
        return report.Id;
    }

    TaskActivationRecordProcessContext CreateProcessContext(string taskActivationRecordId, IEnumerable<TaskExecutionInstanceProcessContext> processContexts)
    {
        return ActivatorUtilities.CreateInstance<TaskActivationRecordProcessContext>(_serviceProvider, taskActivationRecordId, processContexts);
    }

    async ValueTask ProcessTaskExecutionReportsAsync(TaskExecutionReport[] array, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        await _taskActivationRecordExecutor.ExecutionAsync(async (token) =>
        {
            var processContexts = await CollectTaskExecutionProcessContextsAsync(array, cancellationToken);
            if (processContexts.IsDefaultOrEmpty)
            {
                return;
            }

            var taskActivateRecordProcessContexts = processContexts.GroupBy(static x => x.Instance.FireInstanceId)
            .Where(static x => x.Key != null)
            .Select(x => CreateProcessContext(x.Key, x)).ToImmutableArray();
            if (Debugger.IsAttached)
            {
                foreach (var context in taskActivateRecordProcessContexts)
                {
                    await ProcessAsync(context, cancellationToken);
                }
            }
            else
            {
                await Parallel.ForEachAsync(taskActivateRecordProcessContexts, new ParallelOptions()
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = 4
                }, ProcessAsync);
            }

            var taskActivationRecords = taskActivateRecordProcessContexts.Where(static x => x.TaskActivationRecord != null).Select(static x => x.TaskActivationRecord!);
            await ProcessTaskFlowActiveRecordListAsync(taskActivationRecords, cancellationToken);
        }, cancellationToken);
        stopwatch.Stop();
        var ellapsed = stopwatch.Elapsed;
    }

    async ValueTask ProcessAsync(TaskActivationRecordProcessContext processContext, CancellationToken cancellationToken = default)
    {
        await processContext.ProcessAsync(cancellationToken);
    }

    private async ValueTask<ImmutableArray<TaskExecutionInstanceProcessContext>> CollectTaskExecutionProcessContextsAsync(TaskExecutionReport[] array, CancellationToken cancellationToken)
    {
        var taskExecutionInstanceIdList = FilterNull(array.Select(GetTaskId).Distinct()).ToArray();
        var taskExecutionInstanceIdFilters = DataFilterCollection<string>.Includes(taskExecutionInstanceIdList);
        var taskExecutionInstanceList = await GetTaskExeuctionInstanceListAsync(taskExecutionInstanceIdFilters, cancellationToken);

        var builder = ImmutableArray.CreateBuilder<TaskExecutionInstanceProcessContext>(taskExecutionInstanceIdList.Length);
        foreach (var reportGroup in array.GroupBy(GetTaskId))
        {
            var id = reportGroup.Key;
            if (id == null)
            {
                continue;
            }
            TaskExecutionInstanceModel? taskExecutionInstance = null;
            foreach (var item in taskExecutionInstanceList)
            {
                if (item.Id == id)
                {
                    taskExecutionInstance = item;
                    break;
                }
            }
            if (taskExecutionInstance == null)
            {
                continue;
            }
            var context = new TaskExecutionInstanceProcessContext(taskExecutionInstance, [.. reportGroup]);
            builder.Add(context);
        }
        var processContexts = builder.ToImmutable();
        return processContexts;
    }



    async ValueTask<TaskActivationRecordModel?> QueryActivationRecordAsync(
        string taskActivationRecordId,
        CancellationToken cancellationToken = default)
    {
        await using var taskActivationRecordRepo = await _taskActivationRecordRepoFactory.CreateRepositoryAsync(cancellationToken);
        var taskActivationRecord = await taskActivationRecordRepo.GetByIdAsync(taskActivationRecordId, cancellationToken);
        _webServerCounter.TaskExecutionReportQueryTimeSpan.Value += taskActivationRecordRepo.LastOperationTimeSpan;
        return taskActivationRecord;
    }

    async ValueTask<List<TaskExecutionInstanceModel>> GetTaskExeuctionInstanceListAsync(
        DataFilterCollection<string> taskExecutionInstanceIdFilters,
        CancellationToken cancellationToken = default)
    {
        await using var taskExecutionInstanceRepo = await _taskExecutionInstanceRepoFactory.CreateRepositoryAsync(cancellationToken);
        var list = await taskExecutionInstanceRepo.ListAsync(
                                                                    new TaskExecutionInstanceListSpecification(taskExecutionInstanceIdFilters),
                                                                    cancellationToken);
        _webServerCounter.TaskExecutionReportQueryTimeSpan.Value += taskExecutionInstanceRepo.LastOperationTimeSpan;
        return list;
    }


    static IEnumerable<string> FilterNull(IEnumerable<string?> taskIdList)
    {
        if (taskIdList==null)
        {
            yield break;
        }
        foreach (var item in taskIdList)
        {
            if (item == null)
            {
                continue;
            }
            yield return item;
        }
        yield break;
    }






    async ValueTask ProcessExcutionTimeLimitAsync(
        string taskExecutionInstanceId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var taskExecutionInstanceRepo = await _taskExecutionInstanceRepoFactory.CreateRepositoryAsync(cancellationToken);
            var taskExecutionInstance = await taskExecutionInstanceRepo.GetByIdAsync(taskExecutionInstanceId, cancellationToken);
            if (taskExecutionInstance != null)
            {
                if (taskExecutionInstance.IsTerminatedStatus())
                {
                    return;
                }
                _taskExecutionReportBatchQueue.Post(new TaskExecutionReportMessage()
                {
                    NodeSessionId = new Infrastructure.NodeSessions.NodeSessionId(taskExecutionInstance.NodeInfoId),
                    Message = new TaskExecutionReport()
                    {
                        Id = taskExecutionInstanceId,
                        Message = "pendding cancel",
                        Status = TaskExecutionStatus.PenddingCancel,
                        RequestId = Guid.NewGuid().ToString()
                    }
                });
                await _taskCancellationBatchQueue.SendAsync(new TaskCancellationParameters()
                {
                    TaskExeuctionInstanceId = taskExecutionInstanceId,
                    Source = Dns.GetHostName(),
                    Context = nameof(TaskExecutionReportConsumerService)
                }, cancellationToken);

            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex, taskExecutionInstanceId);
        }
    }





    private async ValueTask CancelChildTasksAsync(
         TaskExecutionInstanceModel parentTaskInstance,
        CancellationToken cancellationToken = default)
    {
        if (parentTaskInstance.ChildTaskScheduleCount == 0)
        {
            return;
        }

        await using var taskActivationRecordRepo = await _taskActivationRecordRepoFactory.CreateRepositoryAsync();
        var taskActivationRecord = await taskActivationRecordRepo.GetByIdAsync(parentTaskInstance.FireInstanceId, cancellationToken);

        if (taskActivationRecord == null)
        {
            _logger.LogError($"Could not found task fire config:{parentTaskInstance.FireInstanceId}");
            return;
        }



        var taskDefinition = JsonSerializer.Deserialize<TaskDefinition>(taskActivationRecord.TaskDefinitionJson);
        if (taskDefinition == null) return;

        foreach (var childTaskDefinitionEntry in taskDefinition.ChildTaskDefinitions)
        {
            var taskDefinitionId = childTaskDefinitionEntry.Value;
            if (taskDefinitionId == null)
            {
                continue;
            }
            var childTaskDefinition = await FindTaskDefinitionAsync(taskDefinitionId, cancellationToken);
            if (childTaskDefinition == null)
            {
                continue;
            }
            var spec = new TaskExecutionInstanceListSpecification(
                    DataFilterCollection<TaskExecutionStatus>.Includes(
                    [
                        TaskExecutionStatus.Triggered,
                        TaskExecutionStatus.Running
                    ]),
                    DataFilterCollection<string>.Includes([parentTaskInstance.Id]),
                    DataFilterCollection<string>.Includes([childTaskDefinitionEntry.Id])
                    );

            await foreach (var instance in QueryTaskExecutionInstanceListAsync(spec, cancellationToken))
            {
                await _taskCancellationBatchQueue.SendAsync(new TaskCancellationParameters(
                    instance.Id,
                    nameof(CancelChildTasksAsync),
                    Dns.GetHostName()), cancellationToken);
                if (instance.ChildTaskScheduleCount == 0)
                {
                    continue;
                }
                _ = Task.Run(async () =>
                 {
                     await CancelChildTasksAsync(instance, cancellationToken);
                 });
            }


        }
    }

    async IAsyncEnumerable<TaskExecutionInstanceModel> QueryTaskExecutionInstanceListAsync(
        TaskExecutionInstanceListSpecification spec,
        [EnumeratorCancellation]CancellationToken cancellationToken = default)
    {
        await using var taskExecutionInstanceRepo = await _taskExecutionInstanceRepoFactory.CreateRepositoryAsync(cancellationToken);
        var instances = taskExecutionInstanceRepo.AsAsyncEnumerable(spec);
        await foreach (var instance in instances)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
            yield return instance;
        }
        yield break;
    }

    async ValueTask<TaskDefinitionModel?> FindTaskDefinitionAsync(
        string taskDefinitionId,
        CancellationToken cancellationToken)
    {
        var result = await _configurationQueryService.QueryConfigurationByIdListAsync<TaskDefinitionModel>(
            [taskDefinitionId],
            cancellationToken);
        return result.Items?.FirstOrDefault();
    }

    async ValueTask ProcessExpiredTaskExecutionInstanceAsync(CancellationToken cancellationToken = default)
    {
        try
        {

            var pageIndex = 1;
            var pageSize = 100;
            while (true)
            {


                var listQueryResult = await QueryTaskExecutionInstanceListAsync(
                    pageIndex,
                    pageSize,
                    cancellationToken);

                var taskExeuctionInstances = listQueryResult.Items;

                if (!listQueryResult.HasValue)
                {
                    break;
                }

                await ProcessTaskExecutionInstanceListAsync(taskExeuctionInstances, cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                if (listQueryResult.Items.Count() < pageSize)
                {
                    break;
                }
                pageIndex++;
            }

        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    async ValueTask<ListQueryResult<TaskExecutionInstanceModel>> QueryTaskExecutionInstanceListAsync(
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        await using var taskExecutionInstanceRepo = await _taskExecutionInstanceRepoFactory.CreateRepositoryAsync();
        var listQueryResult = await taskExecutionInstanceRepo.PaginationQueryAsync(
                                     new TaskExecutionInstanceListSpecification(
                                         DataFilterCollection<TaskExecutionStatus>.Includes(
                                         [
                                             TaskExecutionStatus.Triggered,
                                            TaskExecutionStatus.Started,
                                            TaskExecutionStatus.Running
                                         ]),
                                         false),
                                     new PaginationInfo(pageIndex, pageSize),
                                     cancellationToken);
        return listQueryResult;
    }

    async ValueTask ProcessTaskExecutionInstanceListAsync(
        IEnumerable<TaskExecutionInstanceModel> taskExecutionInstances,
        CancellationToken cancellationToken = default)
    {
        foreach (var taskExecutionInstanceGroup in taskExecutionInstances.GroupBy(static x => x.FireInstanceId))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1000), cancellationToken);
            var taskFireInstanceId = taskExecutionInstanceGroup.Key;
            if (taskFireInstanceId == null) continue;
            var taskActiveRecord = await QueryTaskActiveRecordAsync(taskFireInstanceId, cancellationToken);
            if (taskActiveRecord == null) continue;
            var taskDefinition = taskActiveRecord.GetTaskDefinition();
            if (taskDefinition == null) continue;
            foreach (var taskExecutionInstance in taskExecutionInstanceGroup)
            {
                if (taskExecutionInstance.FireTimeUtc == DateTime.MinValue)
                {
                    continue;
                }

                if (DateTime.UtcNow - taskExecutionInstance.FireTimeUtc < TimeSpan.FromDays(7))
                {
                    continue;
                }
                else
                {
                    if (taskExecutionInstance.FireTimeUtc + TimeSpan.FromSeconds(taskDefinition.ExecutionLimitTimeSeconds) < DateTime.UtcNow)
                    {
                        await _taskCancellationBatchQueue.SendAsync(new TaskCancellationParameters(
                            taskExecutionInstance.Id,
                            nameof(TaskExecutionReportConsumerService),
                            Dns.GetHostName()), cancellationToken);
                        await CancelChildTasksAsync(taskExecutionInstance, cancellationToken);
                    }
                }

            }

        }
    }

    async ValueTask<TaskActivationRecordModel?> QueryTaskActiveRecordAsync(
    string fireInstanceId,
    CancellationToken cancellationToken = default)
    {
        await using var taskActiveRecordRepo = await _taskActivationRecordRepoFactory.CreateRepositoryAsync(cancellationToken);
        var taskActiveRecord = await taskActiveRecordRepo.GetByIdAsync(fireInstanceId, cancellationToken);
        return taskActiveRecord;
    }


}