using Ardalis.Specification;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Repositories
{
    public class SemaphoreEFRepository<TEntity,TDbContext> : EFRepository<TEntity, TDbContext>
        where TEntity : class, IAggregateRoot
        where TDbContext :DbContext
    {
        readonly SemaphoreSlim _semaphoreSlim;

        public SemaphoreEFRepository(TDbContext dbContext,SemaphoreSlim semaphoreSlim) : base(dbContext)
        {
            _semaphoreSlim = semaphoreSlim;
        }

        public override void Dispose()
        {
            _semaphoreSlim.Release();
            base.Dispose();
        }

        public override ValueTask DisposeAsync()
        {
            _semaphoreSlim.Release();
            return base.DisposeAsync();
        }
    }
}
