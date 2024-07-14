namespace NodeService.WebServer.Services.DataQueue
{

    public partial class DataQueueService<TEntity> where TEntity : RecordBase
    {
        public class AsyncOperation : AsyncOperation<DataQueueServiceParameters<TEntity>, DataQueueServiceResult<TEntity>>
        {
            public AsyncOperation(
                DataQueueServiceParameters<TEntity> argument,
                AsyncOperationKind kind,
                AsyncOperationPriority priority = AsyncOperationPriority.Normal,
                CancellationToken cancellationToken = default) : base(argument, kind, priority, cancellationToken)
            {

            }
        }
    }
}
