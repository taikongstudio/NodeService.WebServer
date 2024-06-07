namespace NodeService.WebServer.Services.Counters;

public class WebServerCounter
{
    public ulong TaskExecutionReportAvailableCount { get; set; }
    public ulong TaskExecutionReportSaveChangesCount { get; set; }
    public ulong HeartBeatAvailableCount { get; set; }
    public ulong TaskExecutionReportProcessLogEntriesCount { get; set; }
    public ulong HeartBeatConsumeCount { get; set; }
    public ulong TaskExecutionReportConsumeCount { get; set; }

    public TimeSpan TaskExecutionReportTotalTimeSpan { get; set; }

    public TimeSpan TaskExecutionReportQueryTimeSpan { get; set; }

    public TimeSpan TaskExecutionReportSaveTimeSpan { get; set; }

    public TimeSpan TaskExecutionReportProcessLogEntriesTimeSpan { get; set; }

    public TimeSpan TaskExecutionReportSaveLogEntriesTimeSpan { get; set; }

    public TimeSpan TaskExecutionReportSaveLogEntriesMaxTimeSpan { get; set; }

    public ulong TaskExecutionReportLogEntriesCount { get; set; }

    public ulong TaskExecutionReportLogEntriesPageCount { get; set; }

    public ulong TaskExecutionReportLogGroupAvailableCount { get; set; }

    public ulong TaskExecutionReportLogGroupConsumeCount { get; set; }

    public TimeSpan TaskExecutionReportProcessTimeSpan { get; set; }
    public TimeSpan HeartBeatTotalProcessTimeSpan { get; set; }
    public ulong NodeServiceInputMessagesCount { get; set; }
    public ulong NodeServiceOutputMessagesCount { get; set; }
    public ulong NodeServiceExpiredMessagesCount { get; set; }
}