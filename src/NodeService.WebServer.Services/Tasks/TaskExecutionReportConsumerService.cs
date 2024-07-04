using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Hosting;
using NodeService.Infrastructure.Logging;
using NodeService.Infrastructure.Models;
using NodeService.WebServer.Data;
using NodeService.WebServer.Data.Repositories;
using NodeService.WebServer.Data.Repositories.Specifications;
using NodeService.WebServer.Services.Counters;
using NodeService.WebServer.Services.NodeFileSystem;
using NodeService.WebServer.Services.NodeSessions;
using System.Buffers;
using System.Collections.Immutable;
using System.Net;

namespace NodeService.WebServer.Services.Tasks;

public partial class TaskExecutionReportConsumerService : BackgroundService
{
    class TaskExecutionReportProcessContext
    {
        public string? TaskId { get; init; }

        public IEnumerable<TaskExecutionReportMessage> Messages { get; init; }

        public TaskExecutionInstanceModel? TaskExecutionInstance { get; set; }

        public bool StatusChanged { get; set; }

        public bool MessageChanged { get; set; }
    }


    readonly ExceptionCounter _exceptionCounter;
    readonly TaskFlowExecutor _taskFlowExecutor;
    readonly ILogger<TaskExecutionReportConsumerService> _logger;
    readonly IMemoryCache _memoryCache;
    readonly ApplicationRepositoryFactory<TaskDefinitionModel> _taskDefinitionRepoFactory;
    readonly ApplicationRepositoryFactory<TaskExecutionInstanceModel> _taskExecutionInstanceRepoFactory;
    readonly ApplicationRepositoryFactory<TaskActivationRecordModel> _taskActivationRecordRepoFactory;
    readonly ApplicationRepositoryFactory<TaskFlowExecutionInstanceModel> _taskFlowExecutionInstanceRepoFactory;
    readonly ApplicationRepositoryFactory<TaskFlowTemplateModel> _taskFlowTemplateRepoFactory;
    readonly BatchQueue<TaskLogUnit> _taskLogUnitBatchQueue;
    readonly BatchQueue<TaskActivateServiceParameters> _taskScheduleAsyncQueue;
    readonly BatchQueue<TaskCancellationParameters> _taskCancellationBatchQueue;
    readonly BatchQueue<TaskExecutionReportProcessContext[]> _reportProcessContextBatchQueue;
    readonly BatchQueue<TaskExecutionReportMessage> _taskExecutionReportBatchQueue;
    readonly TaskScheduler _taskScheduler;
    readonly TaskSchedulerDictionary _taskSchedulerDictionary;
    readonly WebServerCounter _webServerCounter;

    public TaskExecutionReportConsumerService(
        ApplicationRepositoryFactory<TaskExecutionInstanceModel> taskExecutionInstanceRepositoryFactory,
        ApplicationRepositoryFactory<TaskFlowExecutionInstanceModel> taskFlowExecutionInstanceRepoFactory,
        ApplicationRepositoryFactory<TaskDefinitionModel> taskDefinitionRepositoryFactory,
        ApplicationRepositoryFactory<TaskFlowTemplateModel> taskFlowTemplateRepoFactory,
        ApplicationRepositoryFactory<TaskActivationRecordModel> taskActivationRecordRepoFactory,
        BatchQueue<TaskExecutionReportMessage> taskExecutionReportBatchQueue,
        BatchQueue<TaskLogUnit> taskLogUnitBatchQueue,
        BatchQueue<TaskCancellationParameters> taskCancellationBatchQueue,
        BatchQueue<TaskActivateServiceParameters> taskScheduleAsyncQueue,
        ILogger<TaskExecutionReportConsumerService> logger,
        TaskSchedulerDictionary taskSchedulerDictionary,
        TaskScheduler taskScheduler,
        IMemoryCache memoryCache,
        WebServerCounter webServerCounter,
        ExceptionCounter exceptionCounter,
        TaskFlowExecutor taskFlowExecutor
    )
    {
        _taskExecutionInstanceRepoFactory = taskExecutionInstanceRepositoryFactory;
        _taskDefinitionRepoFactory = taskDefinitionRepositoryFactory;
        _taskActivationRecordRepoFactory = taskActivationRecordRepoFactory;
        _taskFlowExecutionInstanceRepoFactory = taskFlowExecutionInstanceRepoFactory;
        _taskFlowTemplateRepoFactory = taskFlowTemplateRepoFactory;
        _taskExecutionReportBatchQueue = taskExecutionReportBatchQueue;
        _taskScheduleAsyncQueue = taskScheduleAsyncQueue;
        _taskCancellationBatchQueue = taskCancellationBatchQueue;
        _reportProcessContextBatchQueue = new BatchQueue<TaskExecutionReportProcessContext[]>(64, TimeSpan.FromSeconds(3));
        _logger = logger;
        _taskSchedulerDictionary = taskSchedulerDictionary;
        _taskScheduler = taskScheduler;
        _taskLogUnitBatchQueue = taskLogUnitBatchQueue;
        _memoryCache = memoryCache;
        _webServerCounter = webServerCounter;
        _exceptionCounter = exceptionCounter;
        _taskFlowExecutor = taskFlowExecutor;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(
            ProcessExipredTasksAsync(cancellationToken),
            ProcessTaskExecutionReportAsync(cancellationToken),
            ProcessContextAsync(cancellationToken));
    }

    private async Task ProcessExipredTasksAsync(CancellationToken cancellationToken=default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await ProcessExpiredTaskExecutionInstanceAsync(cancellationToken);
            await Task.Delay(TimeSpan.FromHours(6), cancellationToken);
        }
    }

    private async Task ProcessTaskExecutionReportAsync(CancellationToken cancellationToken=default)
    {
        await foreach (var array in _taskExecutionReportBatchQueue.ReceiveAllAsync(cancellationToken))
        {
            if (array == null)
            {
                continue;
            }
            try
            {
                var stopwatch = new Stopwatch();
                stopwatch.Start();
                await ProcessTaskExecutionReportsAsync(array, cancellationToken);
                stopwatch.Stop();
                _logger.LogInformation(
                    $"process {array.Length} messages,spent: {stopwatch.Elapsed}, AvailableCount:{_taskExecutionReportBatchQueue.AvailableCount}");
                _webServerCounter.TaskExecutionReportAvailableCount.Value = (uint)_taskExecutionReportBatchQueue.AvailableCount;
                _webServerCounter.TaskExecutionReportTotalTimeSpan.Value += stopwatch.Elapsed;
                _webServerCounter.TaskExecutionReportConsumeCount.Value += (uint)array.Length;
                stopwatch.Reset();
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

    private static string? GetTaskId(TaskExecutionReportMessage? message)
    {
        if (message == null) return null;
        var report = message.GetMessage();
        if (report == null) return null;
        if (report.Properties.TryGetValue("Id", out var id)) return id;
        return report.Id;
    }



    private static LogEntry Convert(TaskExecutionLogEntry taskExecutionLogEntry)
    {
        return new LogEntry
        {
            DateTimeUtc = taskExecutionLogEntry.DateTime.ToDateTime().ToUniversalTime(),
            Type = (int)taskExecutionLogEntry.Type,
            Value = taskExecutionLogEntry.Value
        };
    }

    static TaskExecutionReportProcessContext CreateProcessContext(IGrouping<string?, TaskExecutionReportMessage> group)
    {
        return new TaskExecutionReportProcessContext() { TaskId = group.Key, Messages = group };
    }

    private async Task ProcessTaskExecutionReportsAsync(
        TaskExecutionReportMessage[] array,
        CancellationToken cancellationToken = default)
    {
        try
        {

            var contexts = array.GroupBy(GetTaskId).Select(CreateProcessContext).ToArray();

            await Parallel.ForEachAsync(contexts, new ParallelOptions()
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Math.Max(array.Length / 10, 8),
            }, ProcessTaskExecutionReportGroupAsync);

            await _reportProcessContextBatchQueue.SendAsync(contexts, cancellationToken);
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


    async ValueTask ProcessTaskExecutionReportGroupAsync(
        TaskExecutionReportProcessContext  processContext,
         CancellationToken cancellationToken)
    {
        try
        {
            var taskId = processContext.TaskId;
            if (taskId == null) return;
            var stopwatchSave = new Stopwatch();
            var stopwatchQuery = new Stopwatch();
            var stopwatchProcessLogEntries = new Stopwatch();
            var stopwatchProcessMessage = new Stopwatch();
            var stopwatchSaveTimeSpan = TimeSpan.Zero;
            using var taskExecutionInstanceRepo = _taskExecutionInstanceRepoFactory.CreateRepository();
            using var taskActivationRecordRepo = _taskActivationRecordRepoFactory.CreateRepository();

            stopwatchQuery.Restart();
            var taskExecutionInstance = await taskExecutionInstanceRepo.GetByIdAsync(taskId, cancellationToken);
            stopwatchQuery.Stop();
            _logger.LogInformation($"{taskId}:QueryElapsed:{stopwatchQuery.Elapsed}");
            _webServerCounter.TaskExecutionReportQueryTimeSpan.Value += stopwatchQuery.Elapsed;

            if (taskExecutionInstance == null) return;
            processContext.TaskExecutionInstance = taskExecutionInstance;
            if (taskExecutionInstance.IsTerminatedStatus()) return;

            var logEntriesRecieveCount = taskExecutionInstance.LogEntriesRecieveCount;
            var logEntriesSaveCount = taskExecutionInstance.LogEntriesSaveCount;
            stopwatchProcessLogEntries.Restart();
            foreach (var reportMessage in processContext.Messages)
            {
                if (reportMessage == null) continue;
                var report = reportMessage.GetMessage();
                if (report.LogEntries.Count > 0)
                {
                    _webServerCounter.TaskLogUnitRecieveCount.Value++;
                    var taskLogUnit = new TaskLogUnit
                    {
                        Id = taskId,
                        LogEntries = report.LogEntries.Select(Convert).ToImmutableArray()
                    };
                    taskExecutionInstance.LogEntriesRecieveCount += taskLogUnit.LogEntries.Length;
                    await _taskLogUnitBatchQueue.SendAsync(taskLogUnit, cancellationToken);
                    _logger.LogInformation($"Send task log unit:{taskId},{taskLogUnit.LogEntries.Length} enties");
                }
            }

            stopwatchProcessLogEntries.Stop();
            _webServerCounter.TaskLogUnitEntriesTimeSpan.Value += stopwatchProcessLogEntries.Elapsed;


            var taskExecutionStatus = taskExecutionInstance.Status;
            var messsage = taskExecutionInstance.Message;
            var executionBeginTime = taskExecutionInstance.ExecutionBeginTimeUtc;
            var executionEndTime = taskExecutionInstance.ExecutionEndTimeUtc;

            foreach (var messageStatusGroup in processContext.Messages.GroupBy(static x => x.GetMessage().Status).OrderBy(static x => x.Key))
            {
                stopwatchProcessMessage.Restart();
                var status = messageStatusGroup.Key;
                foreach (var reportMessage in messageStatusGroup)
                {
                    if (reportMessage == null) continue;
                    await ProcessTaskExecutionReportAsync(
                        taskExecutionInstance,
                        reportMessage,
                        cancellationToken);
                }

                stopwatchProcessMessage.Stop();
                _logger.LogInformation($"process {status} {messageStatusGroup.Count()} messages,spent:{stopwatchProcessMessage.Elapsed}");
                _webServerCounter.TaskExecutionReportProcessTimeSpan.Value += stopwatchProcessMessage.Elapsed;
            }

            int diffCount = 0;

            if (logEntriesRecieveCount != taskExecutionInstance.LogEntriesRecieveCount)
            {
                diffCount++;
            }

            if (taskExecutionStatus != taskExecutionInstance.Status)
            {
                processContext.StatusChanged = true;
                _logger.LogInformation($"{taskId} StatusChanged:{taskExecutionStatus}=>{taskExecutionInstance.Status}");
                diffCount++;
            }

            if (messsage != taskExecutionInstance.Message)
            {
                processContext.MessageChanged = true;
                _logger.LogInformation($"{taskId} StatusChanged:{messsage}=>{taskExecutionInstance.Message}");
                messsage = taskExecutionInstance.Message;
                diffCount++;
            }

            if (executionBeginTime != taskExecutionInstance.ExecutionBeginTimeUtc)
            {
                _logger.LogInformation($"{taskId} StatusChanged:{executionBeginTime}=>{taskExecutionInstance.ExecutionBeginTimeUtc}");
                diffCount++;
            }

            if (executionEndTime != taskExecutionInstance.ExecutionEndTimeUtc)
            {
                _logger.LogInformation($"{taskId} StatusChanged:{executionEndTime}=>{taskExecutionInstance.ExecutionEndTimeUtc}");
                diffCount++;
            }

            if (diffCount > 0)
            {
                stopwatchSave.Restart();

                var changesCount = await taskExecutionInstanceRepo.SaveChangesAsync(cancellationToken);

                stopwatchSave.Stop();
                stopwatchSaveTimeSpan += stopwatchSave.Elapsed;
                _webServerCounter.TaskExecutionReportSaveTimeSpan.Value += stopwatchSave.Elapsed;
                _webServerCounter.TaskExecutionReportSaveChangesCount .Value+= (uint)changesCount;
                _logger.LogInformation($"save {taskId} ,spent:{stopwatchSave.Elapsed}");
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    private async ValueTask CancelTimeLimitTaskAsync(TaskSchedulerKey jobSchedulerKey)
    {
        if (!_taskSchedulerDictionary.TryRemove(jobSchedulerKey, out var asyncDisposable)) return;
        if (asyncDisposable != null) await asyncDisposable.DisposeAsync();
    }

    private async Task ScheduleTimeLimitTaskAsync(
        IEnumerable<TaskExecutionInstanceModel> taskExecutionInstances,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (taskExecutionInstances == null || !taskExecutionInstances.Any()) return;
            using var taskDefinitionRepo = _taskDefinitionRepoFactory.CreateRepository();
            foreach (var taskExecutionInstanceGroup in
                     taskExecutionInstances.GroupBy(static x => x.TaskDefinitionId))
            {
                var taskDefinitionId = taskExecutionInstanceGroup.Key;
                if (taskDefinitionId == null) continue;
                var taskDefinition = await taskDefinitionRepo.GetByIdAsync(taskDefinitionId, cancellationToken);
                if (taskDefinition == null) continue;
                foreach (var taskExecutionInstance in taskExecutionInstanceGroup)
                {
                    if (taskDefinition == null || taskDefinition.ExecutionLimitTimeSeconds <= 0) continue;

                    var key = new TaskSchedulerKey(
                        taskExecutionInstance.Id,
                        TriggerSource.Manual,
                        nameof(TaskExecutionTimeLimitJob));
                    await _taskScheduler.ScheduleAsync<TaskExecutionTimeLimitJob>(key,
                        TriggerBuilderHelper.BuildDelayTrigger(
                            TimeSpan.FromSeconds(taskDefinition.ExecutionLimitTimeSeconds)),
                        new Dictionary<string, object?>
                        {
                            {
                                "TaskExecutionInstance",
                                taskExecutionInstance.JsonClone<TaskExecutionInstanceModel>()
                            }
                        }, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    private async Task ProcessTaskExecutionReportAsync(
        TaskExecutionInstanceModel taskExecutionInstance,
        NodeSessionMessage<TaskExecutionReport> message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var report = message.GetMessage();


            var taskExecutionTimeLimitJobKey = new TaskSchedulerKey(
                taskExecutionInstance.Id,
                TriggerSource.Schedule,
                nameof(TaskExecutionTimeLimitJob)
            );
            switch (report.Status)
            {
                case TaskExecutionStatus.Unknown:
                    break;
                case TaskExecutionStatus.Triggered:
                    break;
                case TaskExecutionStatus.Pendding:
                    break;
                case TaskExecutionStatus.Started:
                    if (taskExecutionInstance.Status != TaskExecutionStatus.Started)
                        await ScheduleTimeLimitTaskAsync([taskExecutionInstance], cancellationToken);
                    break;
                case TaskExecutionStatus.Running:
                    break;
                case TaskExecutionStatus.Failed:
                case TaskExecutionStatus.Cancelled:
                    if (!taskExecutionInstance.IsTerminatedStatus())
                    {
                        await CancelTimeLimitTaskAsync(taskExecutionTimeLimitJobKey);
                    }

                    break;
                case TaskExecutionStatus.Finished:
                    if (!taskExecutionInstance.IsTerminatedStatus())
                    {
                        await ScheduleChildTasksAsync(taskExecutionInstance, cancellationToken);
                        await CancelTimeLimitTaskAsync(taskExecutionTimeLimitJobKey);
                    }
                    break;
                case TaskExecutionStatus.PenddingTimeout:
                    break;
            }


            switch (report.Status)
            {
                case TaskExecutionStatus.Unknown:
                    break;
                case TaskExecutionStatus.Triggered:
                case TaskExecutionStatus.Pendding:
                    break;
                case TaskExecutionStatus.Started:
                    taskExecutionInstance.ExecutionBeginTimeUtc = DateTime.UtcNow;
                    break;
                case TaskExecutionStatus.Running:
                    break;
                case TaskExecutionStatus.Failed:
                case TaskExecutionStatus.Finished:
                case TaskExecutionStatus.Cancelled:
                    taskExecutionInstance.ExecutionEndTimeUtc = DateTime.UtcNow;
                    break;
            }

            if (taskExecutionInstance.Status < report.Status && report.Status != TaskExecutionStatus.PenddingTimeout)
            {
                taskExecutionInstance.Status = report.Status;
            }

            if (report.Status == TaskExecutionStatus.PenddingTimeout)
            {
                taskExecutionInstance.Status = report.Status;
            }
            if (!string.IsNullOrEmpty(report.Message))
            {
                var key = $"{nameof(TaskCancellationQueueService)}:{taskExecutionInstance.Id}";
                if (report.Status == TaskExecutionStatus.Cancelled
                    &&
                    _memoryCache.TryGetValue<TaskCancellationParameters>(key, out var parameters) && parameters != null)
                {
                    _memoryCache.Remove(key);
                    taskExecutionInstance.Message = $"{nameof(parameters.Source)}: {parameters.Source},{nameof(parameters.Host)}: {parameters.Host}";
                }
                else
                {
                    taskExecutionInstance.Message = report.Message;
                }
            }
        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }


    private async Task ScheduleChildTasksAsync(
        TaskExecutionInstanceModel parentTaskInstance,
        CancellationToken cancellationToken = default)
    {
        using var taskActivationRecordRepo = _taskActivationRecordRepoFactory.CreateRepository();
        var taskActivationRecord =
            await taskActivationRecordRepo.GetByIdAsync(parentTaskInstance.FireInstanceId, cancellationToken);

        if (taskActivationRecord == null)
        {
            _logger.LogError($"Could not found task fire config:{parentTaskInstance.FireInstanceId}");
            return;
        }

        using var taskDefinitionRepo = _taskDefinitionRepoFactory.CreateRepository();
        var taskDefinition = JsonSerializer.Deserialize<TaskDefinition>(taskActivationRecord.TaskDefinitionJson);
        if (taskDefinition == null) return;
        foreach (var childTaskDefinition in taskDefinition.ChildTaskDefinitions)
        {
            var childTaskScheduleDefinition = await taskDefinitionRepo.GetByIdAsync(childTaskDefinition.Value, cancellationToken);
            if (childTaskScheduleDefinition == null) continue;
            await _taskScheduleAsyncQueue.SendAsync(new TaskActivateServiceParameters(new FireTaskParameters
            {
                FireTimeUtc = DateTime.UtcNow,
                TriggerSource = TriggerSource.Manual,
                FireInstanceId = $"ChildTask_{Guid.NewGuid()}",
                TaskDefinitionId = childTaskDefinition.Id,
                ScheduledFireTimeUtc = DateTime.UtcNow,
                NodeList = taskDefinition.NodeList,
                ParentTaskId = parentTaskInstance.Id
            }), cancellationToken);
            parentTaskInstance.ChildTaskScheduleCount++;
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
        using var taskActivationRecordRepo = _taskActivationRecordRepoFactory.CreateRepository();
        var taskActivationRecord =
            await taskActivationRecordRepo.GetByIdAsync(parentTaskInstance.FireInstanceId, cancellationToken);

        if (taskActivationRecord == null)
        {
            _logger.LogError($"Could not found task fire config:{parentTaskInstance.FireInstanceId}");
            return;
        }

        using var taskDefinitionRepo = _taskDefinitionRepoFactory.CreateRepository();
        using var taskExecutionInstanceRepo = _taskExecutionInstanceRepoFactory.CreateRepository();
        var taskDefinition = JsonSerializer.Deserialize<TaskDefinition>(taskActivationRecord.TaskDefinitionJson);
        if (taskDefinition == null) return;

        foreach (var childTaskDefinition in taskDefinition.ChildTaskDefinitions)
        {
            var childTaskScheduleDefinition =
                await taskDefinitionRepo.GetByIdAsync(childTaskDefinition.Value, cancellationToken);
            if (childTaskScheduleDefinition == null)
            {
                continue;
            }

            var childTaskExectionInstances = await taskExecutionInstanceRepo.ListAsync(
                new TaskExecutionInstanceSpecification(
                    DataFilterCollection<TaskExecutionStatus>.Includes(
                    [
                        TaskExecutionStatus.Triggered,
                        TaskExecutionStatus.Running
                    ]),
                    DataFilterCollection<string>.Includes([parentTaskInstance.Id]),
                    DataFilterCollection<string>.Includes([childTaskDefinition.Id])
                    ), cancellationToken);
            if (childTaskExectionInstances.Count == 0) continue;

            foreach (var childTaskExectionInstance in childTaskExectionInstances)
            {
                await _taskCancellationBatchQueue.SendAsync(new TaskCancellationParameters(
                    nameof(CancelChildTasksAsync),
                    Dns.GetHostName(),
                    childTaskExectionInstance.Id), cancellationToken);
                await CancelChildTasksAsync(childTaskExectionInstance, cancellationToken);
            }

        }
    }

    async ValueTask ProcessExpiredTaskExecutionInstanceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var taskExecutionInstanceRepo = _taskExecutionInstanceRepoFactory.CreateRepository();
            using var taskDefinitionRepo = _taskDefinitionRepoFactory.CreateRepository();
            var pageIndex = 0;
            var pageSize = 100;
            while (true)
            {
                var listQueryResult = await taskExecutionInstanceRepo.PaginationQueryAsync(
                                            new TaskExecutionInstanceSpecification(
                                                DataFilterCollection<TaskExecutionStatus>.Includes(
                                                [
                                                    TaskExecutionStatus.Triggered,
                                                                TaskExecutionStatus.Started,
                                                                TaskExecutionStatus.Running
                                                ]),
                                                false),
                                            new PaginationInfo(pageIndex, pageSize),
                                            cancellationToken);
                if (listQueryResult.IsEmpty)
                {
                    break;
                }
                pageIndex++;
                var taskExeuctionInstances = listQueryResult.Items;

                await ProcessTaskExecutionInstanceAsync(taskDefinitionRepo, taskExeuctionInstances, cancellationToken);

            }

        }
        catch (Exception ex)
        {
            _exceptionCounter.AddOrUpdate(ex);
            _logger.LogError(ex.ToString());
        }
    }

    private async Task ProcessTaskExecutionInstanceAsync(
        IRepository<TaskDefinitionModel> taskDefinitionRepo,
        IEnumerable<TaskExecutionInstanceModel> taskExeuctionInstances,
        CancellationToken cancellationToken = default)
    {
        List<TaskExecutionInstanceModel> taskExecutionInstanceList = [];
        foreach (var taskExecutionInstanceGroup in
                 taskExeuctionInstances.GroupBy(static x => x.TaskDefinitionId))
        {
            var taskDefinitionId = taskExecutionInstanceGroup.Key;
            if (taskDefinitionId == null) continue;
            var taskDefinition = await taskDefinitionRepo.GetByIdAsync(taskDefinitionId, cancellationToken);
            if (taskDefinition == null) continue;
            taskExecutionInstanceList.Clear();
            foreach (var taskExecutionInstance in taskExecutionInstanceGroup)
            {
                if (taskExecutionInstance.FireTimeUtc == DateTime.MinValue)
                {
                    continue;
                }

                if (DateTime.UtcNow - taskExecutionInstance.FireTimeUtc < TimeSpan.FromDays(7)) continue;
                if (taskExecutionInstance.FireTimeUtc + TimeSpan.FromSeconds(taskDefinition.ExecutionLimitTimeSeconds) < DateTime.UtcNow)
                {
                    await _taskCancellationBatchQueue.SendAsync(new TaskCancellationParameters(
                        nameof(TaskExecutionReportConsumerService),
                        Dns.GetHostName(),
                        taskExecutionInstance.Id), cancellationToken);
                    await CancelChildTasksAsync(taskExecutionInstance, cancellationToken);
                }
                else
                {
                    taskExecutionInstanceList.Add(taskExecutionInstance);
                }
            }

            if (taskExecutionInstanceList.Count > 0)
            {
                await ScheduleTimeLimitTaskAsync(taskExecutionInstanceList, cancellationToken);
            }
        }
    }

}