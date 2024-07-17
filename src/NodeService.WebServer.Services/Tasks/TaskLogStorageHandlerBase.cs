namespace NodeService.WebServer.Services.Tasks
{
    public abstract class TaskLogStorageHandlerBase
    {

    public long TotalGroupConsumeCount { get; protected set; }

    public TimeSpan TotalQueryTimeSpan { get; protected set; }

    public TimeSpan TotalSaveMaxTimeSpan { get; protected set; }

    public TimeSpan TotalSaveTimeSpan { get; protected set; }

    public long TotalLogEntriesSavedCount { get; protected set; }

    public long TotalCreatedPageCount { get; protected set; }

    public long TotalDetachedPageCount { get; protected set; }

    public int ActiveTaskLogGroupCount { get; protected set; }

        public abstract ValueTask ProcessAsync(
            IEnumerable<TaskLogUnit> taskLogUnits,
            CancellationToken cancellationToken = default);
    }
}