namespace NodeService.WebServer.Services.Counters;

public class CounterLongValue
{
    private long _value;

    public long Value
    {
        get => Interlocked.Read(ref _value);
        set => Interlocked.Exchange(ref _value, value);
    }

    public override string ToString()
    {
        return this.Value.ToString();
    }
}

public class PartitionOffsetValue
{
    public CounterLongValue Partition { get; set; } = new CounterLongValue();

    public CounterLongValue Offset { get; set; } = new CounterLongValue();

    public string Message { get; set; }

    public override string ToString()
    {
        return $"Partition {Partition.Value} @ {Offset.Value}";
    }

    public static PartitionOffsetValue CreateNew(int partion)
    {
        var value = new PartitionOffsetValue();
        value.Partition.Value = partion;
        return value;
    }
}

public class CounterTimeSpanValue
{
    private long _value;

    public TimeSpan Value
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _value));
        set => Interlocked.Exchange(ref _value, value.Ticks);
    }

    public override string ToString()
    {
        return this.Value.ToString();
    }

}

public class WebServerCounter
{
    const string WebServerCounterSnapshot = nameof(WebServerCounterSnapshot);
    readonly ILogger<WebServerCounter> _logger;

    public WebServerCounter(ILogger<WebServerCounter> logger)
    {
        _logger = logger;
    }

    public WebServerCounterSnapshot Snapshot { get; set; } = new();

    public async ValueTask InitFromCacheAsync(
        ObjectCache objectCache,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = await objectCache.GetObjectAsync<WebServerCounterSnapshot>(WebServerCounterSnapshot, cancellationToken);
            this.Snapshot = snapshot ?? new WebServerCounterSnapshot();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
        }

    }

    public async ValueTask SaveToCacheAsync(
        ObjectCache objectCache,
        CancellationToken cancellationToken = default)
    {
        await objectCache.SetObjectAsync(WebServerCounterSnapshot, Snapshot, cancellationToken);
    }
}

public record class WebServerCounterSnapshot
{
    public CounterLongValue HeartBeatRecieveCount { get; set; } = new();
    public CounterLongValue HeartBeatQueueCount { get; set; } = new();
    public CounterLongValue HeartBeatMessageConsumeCount { get; set; } = new();

    public CounterTimeSpanValue HeartBeatQueryNodeInfoListTimeSpan { get; set; } = new();

    public CounterTimeSpanValue HeartBeatUpdateNodeInfoListTimeSpan { get; set; } = new();

    public CounterTimeSpanValue HeartBeatQueryNodePropsTimeSpan { get; set; } = new();

    public CounterTimeSpanValue HeartBeatSaveNodePropsTimeSpan { get; set; } = new();

    public CounterLongValue TaskExecutionReportRecieveCount { get; set; } = new();
    public CounterLongValue TaskExecutionReportQueueCount { get; set; } = new();
    public CounterLongValue TaskExecutionReportConsumeCount { get; set; } = new();
    public CounterLongValue TaskExecutionReportProducePersistedCount { get; set; } = new();
    public CounterLongValue TaskExecutionReportProduceNotPersistedCount { get; set; } = new();
    public CounterLongValue TaskExecutionReportSaveChangesCount { get; set; } = new();

    public CounterTimeSpanValue TaskExecutionReportTotalTimeSpan { get; set; } = new();

    public CounterTimeSpanValue TaskExecutionReportQueryTimeSpan { get; set; } = new();

    public CounterTimeSpanValue TaskExecutionReportSaveTimeSpan { get; set; } = new();


    public CounterLongValue TaskLogUnitEntriesCount { get; set; } = new();

    public CounterLongValue TaskLogUnitRecieveCount { get; set; } = new();

    public CounterTimeSpanValue TaskLogUnitCollectLogEntriesTimeSpan { get; set; } = new();

    public CounterTimeSpanValue TaskLogUnitSaveTimeSpan { get; set; } = new();

    public CounterTimeSpanValue TaskLogInfoSaveTimeSpan { get; set; } = new();

    public CounterTimeSpanValue TaskLogInfoQueryTimeSpan { get; set; } = new();

    public CounterTimeSpanValue TaskLogUnitQueryTimeSpan { get; set; } = new();

    public CounterTimeSpanValue TaskLogUnitSaveMaxTimeSpan { get; set; } = new();

    public CounterLongValue TaskLogEntriesSaveTimes { get; set; } = new();

    public CounterLongValue TaskLogEntriesSaveCount { get; set; } = new();

    public CounterLongValue TaskLogPageCount { get; set; } = new();

    public CounterLongValue TaskLogUnitQueueCount { get; set; } = new();

    public CounterLongValue TaskLogUnitConsumeCount { get; set; } = new();

    public CounterLongValue TaskLogHandlerCount { get; set; } = new();

    public CounterTimeSpanValue TaskExecutionReportProcessTimeSpan { get; set; } = new();
    public CounterTimeSpanValue HeartBeatTotalProcessTimeSpan { get; set; } = new();
    public CounterLongValue NodeServiceInputMessagesCount { get; set; } = new();
    public CounterLongValue NodeServiceOutputMessagesCount { get; set; } = new();
    public CounterLongValue NodeServiceExpiredMessagesCount { get; set; } = new();

    public CounterLongValue TaskLogPageDetachedCount { get; set; } = new();

    public CounterLongValue NodeFileSyncServiceBatchProcessContextActiveCount { get; set; } = new();

    public CounterLongValue NodeFileSyncServiceBatchProcessContextAddedCount { get; set; } = new();

    public CounterLongValue NodeFileSyncServiceBatchProcessContextRemovedCount { get; set; } = new();

    public CounterTimeSpanValue NodeFileSyncServiceBatchProcessContext_MaxTimeSpan { get; set; } = new();

    public CounterLongValue NodeFileSyncServiceBatchProcessContext_MaxFileLength { get; set; } = new();

    public CounterLongValue KafkaTaskLogConsumeWaitCount { get; set; } = new();

    public CounterLongValue KafkaTaskLogConsumeCount { get; set; } = new();

    public CounterLongValue KafkaTaskLogProduceCount { get; set; } = new();

    public CounterLongValue KafkaTaskLogProduceRetryCount { get; set; } = new();

    public CounterTimeSpanValue KafkaTaskLogConsumeTotalTimeSpan { get; set; } = new();

    public CounterTimeSpanValue KafkaTaskLogConsumeMaxTimeSpan { get; set; } = new();

    public CounterLongValue KafkaTaskLogConsumePrefetchCount { get; set; } = new();

    public CounterLongValue KafkaTaskLogConsumeMaxPrefetchCount { get; set; } = new();

    public CounterTimeSpanValue KafkaTaskLogConsumeContextGroupMaxTimeSpan { get; set; } = new();

    public CounterTimeSpanValue KafkaTaskLogConsumeContextGroupAvgTimeSpan { get; set; } = new();

    public CounterTimeSpanValue KafkaTaskLogConsumeScaleFactor { get; set; } = new();



    public CounterLongValue KafkaConsumeOffset { get; set; } = new();

    public CounterLongValue KafkaProduceOffset { get; set; } = new();

    public ConcurrentDictionary<int, PartitionOffsetValue> KafkaLogProducePartitionOffsetDictionary { get; set; } = new();

    public ConcurrentDictionary<int, PartitionOffsetValue> KafkaLogConsumePartitionOffsetDictionary { get; set; } = new();

    public ConcurrentDictionary<int, PartitionOffsetValue> KafkaDelayMessageProducePartitionOffsetDictionary { get; set; } = new();

    public ConcurrentDictionary<int, PartitionOffsetValue> KafkaDelayMessageConsumePartitionOffsetDictionary { get; set; } = new();

    public ConcurrentDictionary<int, PartitionOffsetValue> TaskExecutionReportProducePartitionOffsetDictionary { get; set; } = new();

    public ConcurrentDictionary<int, PartitionOffsetValue> TaskExecutionReportConsumePartitionOffsetDictionary { get; set; } = new();

    public ConcurrentDictionary<int, PartitionOffsetValue> KafkaTaskObservationEventProducePartitionOffsetDictionary { get; set; } = new();

    public ConcurrentDictionary<int, PartitionOffsetValue> KafkaTaskObservationEventConsumePartitionOffsetDictionary { get; set; } = new();

    public CounterLongValue TaskExecutionReportProduceRetryCount { get; set; } = new();

    public CounterLongValue KafkaDelayMessageTickCount { get; set; } = new();

    public CounterLongValue KafkaDelayMessageScheduleCount { get; set; } = new();

    public CounterLongValue KafkaDelayMessageHandledCount { get; set; } = new();

    public CounterLongValue KafkaDelayMessageProduceCount { get; set; } = new();

    public CounterLongValue KafkaDelayMessageConsumeCount { get; set; } = new();


    public CounterLongValue FireNodeHeathyCheckJobEnqueueCount { get; set; } = new();

    public CounterLongValue FireNodeHeathyCheckJobDequeueCount { get; set; } = new();

    public CounterLongValue NodeHeathyCheckSendEmailCount { get; set; } = new();
    public CounterLongValue NodeHeathyCheckScheduleCount { get; set; } = new();

    public CounterLongValue TaskObservationEventConsumeCount { get; set; } = new();
    public CounterLongValue TaskObservationEventProducePersistedCount { get; set; } = new();

    public CounterLongValue NotificationRecordCount { get; set; } = new();

}