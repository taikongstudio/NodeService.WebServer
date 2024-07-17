namespace NodeService.WebServer.Services.Tasks
{
    public abstract class TaskLogStorageHandlerBase
    {

        public abstract ValueTask ProcessAsync(
            IEnumerable<TaskLogUnit> taskLogUnits,
            CancellationToken cancellationToken = default);
    }
}