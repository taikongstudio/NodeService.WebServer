using OneOf;
using System.Collections.Immutable;

namespace NodeService.WebServer.Services.DataServices
{
    public record class DataQueueServiceResult<TEntity>
    {
        public static implicit operator DataQueueServiceResult<TEntity>(ImmutableArray<TEntity> parameters)
        {
            return new DataQueueServiceResult<TEntity>(parameters);
        }

        public OneOf<ImmutableArray<TEntity>> Result { get; private set; }

        public DataQueueServiceResult(ImmutableArray<TEntity> parameters)
        {
            Result = parameters;
        }



    }
}
