using OneOf;
using System.Collections.Immutable;

namespace NodeService.WebServer.Services.DataQueue
{
    public record class DataQueueServiceParameters<TEntity>
    {
        public static implicit operator DataQueueServiceParameters<TEntity>(ImmutableArray<TEntity> parameters)
        {
            return new DataQueueServiceParameters<TEntity>(parameters);
        }

        public static implicit operator DataQueueServiceParameters<TEntity>(ImmutableArray<string> parameters)
        {
            return new DataQueueServiceParameters<TEntity>(parameters);
        }

        public OneOf<ImmutableArray<TEntity>, ImmutableArray<string>> Parameters { get; private set; }

        public DataQueueServiceParameters(ImmutableArray<TEntity> parameters)
        {
            this.Parameters = parameters;
        }

        public DataQueueServiceParameters(ImmutableArray<string> parameters)
        {
            this.Parameters = parameters;
        }




    }
}
