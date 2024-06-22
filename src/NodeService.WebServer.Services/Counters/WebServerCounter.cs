namespace NodeService.WebServer.Services.Counters;

public class WebServerCounter
{
    public ulong TaskExecutionReportAvailableCount { get; set; }
    public ulong TaskExecutionReportSaveChangesCount { get; set; }
    public ulong HeartBeatAvailableCount { get; set; }
    public ulong TaskLogUnitEntriesCount { get; set; }
    public ulong HeartBeatConsumeCount { get; set; }
    public ulong TaskExecutionReportConsumeCount { get; set; }

    public ulong TaskLogUnitRecieveCount { get; set; }

    public TimeSpan TaskExecutionReportTotalTimeSpan { get; set; }

    public TimeSpan TaskExecutionReportQueryTimeSpan { get; set; }

    public TimeSpan TaskExecutionReportSaveTimeSpan { get; set; }

    public TimeSpan TaskLogUnitEntriesTimeSpan { get; set; }

    public TimeSpan TaskLogUnitSaveTimeSpan { get; set; }

    public TimeSpan TaskLogUnitQueryTimeSpan { get; set; }

    public TimeSpan TaskLogUnitSaveMaxTimeSpan { get; set; }

    public ulong TaskLogEntriesSavedCount { get; set; }

    public ulong TaskLogPageCount { get; set; }

    public ulong TaskLogUnitAvailableCount { get; set; }

    public ulong TaskLogUnitConsumeCount { get; set; }

    public TimeSpan TaskExecutionReportProcessTimeSpan { get; set; }
    public TimeSpan HeartBeatTotalProcessTimeSpan { get; set; }
    public ulong NodeServiceInputMessagesCount { get; set; }
    public ulong NodeServiceOutputMessagesCount { get; set; }
    public ulong NodeServiceExpiredMessagesCount { get; set; }
}