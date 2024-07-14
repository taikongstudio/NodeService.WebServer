using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Repositories
{
    public class LockRepository<TEntity> : EFRepository<TEntity, InMemoryDbContext>
        where TEntity : class, IAggregateRoot
    {
        readonly SemaphoreSlim _semaphoreSlim;
        public LockRepository(InMemoryDbContext dbContext) : base(dbContext)
        {
            _semaphoreSlim = new SemaphoreSlim(1, 1);
        }

        public override async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            try
            {
                await _semaphoreSlim.WaitAsync(cancellationToken);
                return await base.AddAsync(entity, cancellationToken);
            }
            finally
            {
                _semaphoreSlim.Release();
            }

        }

        public override async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await _semaphoreSlim.WaitAsync(cancellationToken);
                return await base.AnyAsync(cancellationToken);
            }
            finally
            {
                _semaphoreSlim.Release();
            }

        }

        public override async Task<bool> AnyAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
        {
            try
            {
                await _semaphoreSlim.WaitAsync(cancellationToken);
                return await base.AnyAsync(specification, cancellationToken);
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }

        public override async Task<IEnumerable<TEntity>> AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            try
            {
                await _semaphoreSlim.WaitAsync(cancellationToken);
                return await base.AddRangeAsync(entities, cancellationToken);
            }
            finally
            {
                _semaphoreSlim.Release();
            }

        }

        public override IAsyncEnumerable<TEntity> AsAsyncEnumerable(ISpecification<TEntity> specification)
        {
            return base.AsAsyncEnumerable(specification);
        }

        public override async Task<int> CountAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await _semaphoreSlim.WaitAsync(cancellationToken);
                return await base.CountAsync(cancellationToken);
            }
            finally
            {
                _semaphoreSlim.Release();
            }

        }

        public override async Task<int> CountAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
        {
            try
            {
                await _semaphoreSlim.WaitAsync(cancellationToken);
                return await base.CountAsync(specification, cancellationToken);
            }
            finally
            {
                _semaphoreSlim.Release();
            }

        }

        public override async Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            try
            {
                await _semaphoreSlim.WaitAsync(cancellationToken);
                await base.DeleteAsync(entity, cancellationToken);
            }
            finally
            {
                _semaphoreSlim.Release();
            }

        }

        public override async Task DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            try
            {
                await _semaphoreSlim.WaitAsync(cancellationToken);
                await base.DeleteRangeAsync(entities, cancellationToken);

            }
            finally
            {
                _semaphoreSlim.Release();
            }

        }

        public override async Task DeleteRangeAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
        {
            try
            {
                await _semaphoreSlim.WaitAsync(cancellationToken);
                await base.DeleteRangeAsync(specification, cancellationToken);
            }
            finally
            {
                _semaphoreSlim.Release();
            }

        }

        public override async Task<TEntity?> FirstOrDefaultAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
        {
            try
            {
                await _semaphoreSlim.WaitAsync(cancellationToken);
                return await base.FirstOrDefaultAsync(specification, cancellationToken);
            }
            finally
            {
                _semaphoreSlim.Release();
            }

        }

        public override async Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<TEntity, TResult> specification, CancellationToken cancellationToken = default) where TResult : default
        {
            try
            {
                await _semaphoreSlim.WaitAsync(cancellationToken);
                return await base.FirstOrDefaultAsync(specification, cancellationToken);
            }
            finally
            {
                _semaphoreSlim.Release();
            }

        }

        public override async Task<TEntity?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default)
        {
            try
            {
                await _semaphoreSlim.WaitAsync(cancellationToken);
                return await base.GetByIdAsync(id, cancellationToken);
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }

        public override async Task<List<TEntity>> ListAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await _semaphoreSlim.WaitAsync(cancellationToken);
                return await base.ListAsync(cancellationToken);
            }
            finally
            {
                _semaphoreSlim.Release();
            }

        }

        public override async Task<List<TEntity>> ListAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
        {
            try
            {
                await _semaphoreSlim.WaitAsync(cancellationToken);
                return await base.ListAsync(specification, cancellationToken);
            }
            finally
            {
                _semaphoreSlim.Release();
            }

        }

        public override async Task<List<TResult>> ListAsync<TResult>(ISpecification<TEntity, TResult> specification, CancellationToken cancellationToken = default)
        {
            try
            {
                await _semaphoreSlim.WaitAsync(cancellationToken);
                return await base.ListAsync(specification, cancellationToken);
            }
            finally
            {
                _semaphoreSlim.Release();
            }


        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await _semaphoreSlim.WaitAsync(cancellationToken);
                return await base.SaveChangesAsync(cancellationToken);
            }
            finally
            {
                _semaphoreSlim.Release();
            }

        }

        public override async Task<TEntity?> SingleOrDefaultAsync(ISingleResultSpecification<TEntity> specification, CancellationToken cancellationToken = default)
        {
            try
            {
                await _semaphoreSlim.WaitAsync(cancellationToken);
                return await base.SingleOrDefaultAsync(specification, cancellationToken);
            }
            finally
            {
                _semaphoreSlim.Release();
            }

        }

        public override async Task<TResult?> SingleOrDefaultAsync<TResult>(ISingleResultSpecification<TEntity, TResult> specification, CancellationToken cancellationToken = default) where TResult : default
        {
            try
            {
                await _semaphoreSlim.WaitAsync(cancellationToken);
                return await base.SingleOrDefaultAsync(specification, cancellationToken);
            }
            finally
            {
                _semaphoreSlim.Release();
            }

        }

        public override async Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            try
            {
                await _semaphoreSlim.WaitAsync(cancellationToken);
                await base.UpdateAsync(entity, cancellationToken);
            }
            finally
            {
                _semaphoreSlim.Release();
            }

        }

        public override async Task UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            try
            {
                await _semaphoreSlim.WaitAsync(cancellationToken);
                await base.UpdateRangeAsync(entities, cancellationToken);
            }
            finally
            {
                _semaphoreSlim.Release();
            }

        }

    }
}
