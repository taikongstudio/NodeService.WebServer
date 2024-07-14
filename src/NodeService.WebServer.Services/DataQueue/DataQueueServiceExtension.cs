using Microsoft.Extensions.DependencyInjection;

namespace NodeService.WebServer.Services.DataQueue
{
    public static class DataQueueServiceExtension
    {
        public static IServiceCollection AddDataQueueService<TEntity>(this IServiceCollection serviceCollection)
            where TEntity : RecordBase
        {
            serviceCollection.AddHostedService<DataQueueService<TEntity>>();
            serviceCollection.AddSingleton<BatchQueue<DataQueueService<TEntity>.AsyncOperation>>();
            return serviceCollection;
        }

        public static IServiceCollection AddDataQueueService<TEntity>(
            this IServiceCollection serviceCollection,
            TimeSpan triggerBatchPeriod,
            int batchSize,
            Func<IServiceCollection, IServiceCollection> func)
    where TEntity : RecordBase
        {
            serviceCollection.AddHostedService<DataQueueService<TEntity>>();
            serviceCollection.AddSingleton((sp) =>
            {
                return new BatchQueue<BatchQueue<DataQueueService<TEntity>.AsyncOperation>>(triggerBatchPeriod, batchSize);
            });
            return func(serviceCollection);
        }

        public static IServiceCollection AddDataQueueService<TEntity>(
            this IServiceCollection serviceCollection,
            Func<IServiceCollection, IServiceCollection> func)
    where TEntity : RecordBase
        {
            serviceCollection.AddHostedService<DataQueueService<TEntity>>();
            serviceCollection.AddSingleton<BatchQueue<AsyncOperation<DataQueueServiceParameters<TEntity>, DataQueueServiceResult<TEntity>>>>();
            return func(serviceCollection);
        }
    }
}
