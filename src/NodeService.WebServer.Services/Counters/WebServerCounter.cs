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

    public CounterLongValue TaskExecutionReportRecieveCount { get; set; } = new();
    public CounterLongValue TaskExecutionReportAvailableCount { get; set; } =new ();
    public CounterLongValue TaskExecutionReportSaveChangesCount { get; set; } = new();
    public CounterLongValue HeartBeatAvailableCount { get; set; } = new();
    public CounterLongValue TaskLogUnitEntriesCount { get; set; } = new();
    public CounterLongValue HeartBeatConsumeCount { get; set; } = new();
    public CounterLongValue TaskExecutionReportConsumeCount { get; set; } = new();

    public CounterLongValue TaskLogUnitRecieveCount { get; set; } = new();

    public CounterTimeSpanValue TaskExecutionReportTotalTimeSpan { get; set; } = new();

    public CounterTimeSpanValue TaskExecutionReportQueryTimeSpan { get; set; } = new();

    public CounterTimeSpanValue TaskExecutionReportSaveTimeSpan { get; set; } = new();

    public CounterTimeSpanValue TaskLogUnitEntriesTimeSpan { get; set; } = new();

    public CounterTimeSpanValue TaskLogUnitSaveTimeSpan { get; set; } = new();

    public CounterTimeSpanValue TaskLogUnitQueryTimeSpan { get; set; } = new();

    public CounterTimeSpanValue TaskLogUnitSaveMaxTimeSpan { get; set; } = new();

    public CounterLongValue TaskLogEntriesSavedCount { get; set; } = new();

    public CounterLongValue TaskLogPageCount { get; set; } = new();

    public CounterLongValue TaskLogUnitAvailableCount { get; set; } = new();

    public CounterLongValue TaskLogUnitConsumeCount { get; set; } = new();

    public CounterTimeSpanValue TaskExecutionReportProcessTimeSpan { get; set; } = new();
    public CounterTimeSpanValue HeartBeatTotalProcessTimeSpan { get; set; } = new();
    public CounterLongValue NodeServiceInputMessagesCount { get; set; } = new();
    public CounterLongValue NodeServiceOutputMessagesCount { get; set; } = new();
    public CounterLongValue NodeServiceExpiredMessagesCount { get; set; } = new();
}