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

    public CounterLongValue KafkaConsumeWaitCount { get; set; } = new();

    public CounterLongValue KafkaConsumeConsumeCount { get; set; } = new();

    public CounterLongValue KafkaProduceCount { get; set; } = new();

    public CounterLongValue KafkaRetryProduceCount { get; set; } = new();

    public CounterTimeSpanValue KafkaTotalConsumeTimeSpan { get; set; } = new();

    public CounterTimeSpanValue KafkaConsumeMaxTimeSpan { get; set; } = new();

    public CounterLongValue KafkaPrefetchCount { get; set; } = new();

    public CounterLongValue KafkaMaxPrefetchCount { get; set; } = new();

    public CounterTimeSpanValue KafkaConsumeContextGroupMaxTimeSpan { get; set; } = new();

    public CounterTimeSpanValue KafkaConsumeContextGroupAvgTimeSpan { get; set; } = new();

    public CounterTimeSpanValue KafkaConsumeScaleFactor { get; set; } = new();

}