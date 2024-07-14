using Ardalis.Specification;
using System.Collections.Generic;
using System.Threading;
using static Grpc.Core.Metadata;

namespace NodeService.WebServer.Data.Repositories
{
    public class InMemoryRepository<TEntity> : EFRepository<TEntity, ApplicationDbContext>
        where TEntity : class, IAggregateRoot
    {

        readonly InMemoryDbContext _dbContext;
        readonly EFRepository<TEntity, InMemoryDbContext> _inMemoryRepo;

        public InMemoryRepository(
            InMemoryDbContext inMemoryDbContext,
           IDbContextFactory<ApplicationDbContext> applicationDbContext) : base(applicationDbContext.CreateDbContext())
        {
            _dbContext = inMemoryDbContext;
            _inMemoryRepo = new EFRepository<TEntity, InMemoryDbContext>(inMemoryDbContext);
        }


        public override async Task<List<TEntity>> ListAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
        {
            var list = await _inMemoryRepo.ListAsync(specification, cancellationToken);
            if (list.Count == 0)
            {
                list = await base.ListAsync(specification, cancellationToken);
                await AddOrUpdateEntitiesAsync(list, cancellationToken);
            }
            return list;
        }

        public override async Task<List<TResult>> ListAsync<TResult>(ISpecification<TEntity, TResult> specification, CancellationToken cancellationToken = default)
        {
            var list = await _inMemoryRepo.ListAsync(specification, cancellationToken);
            if (list.Count == 0)
            {
                list = await base.ListAsync(specification, cancellationToken);
            }
            return list;
        }

        public override async Task<List<TEntity>> ListAsync(CancellationToken cancellationToken = default)
        {
            var list = await _inMemoryRepo.ListAsync(cancellationToken);
            if (list.Count == 0)
            {
                list = await base.ListAsync(cancellationToken);
                await AddOrUpdateEntitiesAsync(list, cancellationToken);
            }
            return list;
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            int localSavedCount = await _inMemoryRepo.SaveChangesAsync(cancellationToken);
            int remoteSavedCount = await base.SaveChangesAsync(cancellationToken);
            return localSavedCount;
        }

        public override async Task<TEntity?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default)
        {
            var entity = await _inMemoryRepo.GetByIdAsync(id, cancellationToken);
            if (entity == null)
            {
                entity = await base.GetByIdAsync(id, cancellationToken);
                if (entity != null)
                {
                    await AddOrUpdateEntitiesAsync([entity], cancellationToken);
                }
            }
            return entity;
        }

        public override async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            await AddOrUpdateEntitiesAsync([entity], cancellationToken);
            return await base.AddAsync(entity, cancellationToken);
        }

        public override async Task<IEnumerable<TEntity>> AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            await AddOrUpdateEntitiesAsync(entities, cancellationToken);
            return await base.AddRangeAsync(entities, cancellationToken);
        }

        public override async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
        {
            bool exists = await _inMemoryRepo.AnyAsync(cancellationToken);
            if (!exists)
            {
                exists = await base.AnyAsync(cancellationToken);
            }
            return exists;
        }

        public override async Task<bool> AnyAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
        {
            bool exists = await _inMemoryRepo.AnyAsync(specification, cancellationToken);
            if (!exists)
            {
                exists = await base.AnyAsync(specification, cancellationToken);
            }
            return exists;
        }

        public override IAsyncEnumerable<TEntity> AsAsyncEnumerable(ISpecification<TEntity> specification)
        {
            return base.AsAsyncEnumerable(specification);
        }

        public override async Task<int> CountAsync(CancellationToken cancellationToken = default)
        {
            var count = await _inMemoryRepo.CountAsync(cancellationToken);
            if (count == 0)
            {
                count = await base.CountAsync(cancellationToken);
            }
            return count;
        }

        public override async Task<int> CountAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
        {
            var count = await _inMemoryRepo.CountAsync(specification, cancellationToken);
            if (count == 0)
            {
                count = await base.CountAsync(specification, cancellationToken);
            }
            return count;
        }

        public override async Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            await _inMemoryRepo.DeleteAsync(entity, cancellationToken);
            await base.DeleteAsync(entity, cancellationToken);
        }

        public override async Task DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            await _inMemoryRepo.DeleteRangeAsync(entities, cancellationToken);
            await base.DeleteRangeAsync(entities, cancellationToken);
        }

        public override async Task DeleteRangeAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
        {
            await _inMemoryRepo.DeleteRangeAsync(specification, cancellationToken);
            await base.DeleteRangeAsync(specification, cancellationToken);
        }

        public override async Task<TEntity?> FirstOrDefaultAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
        {
            var entity = await _inMemoryRepo.FirstOrDefaultAsync(specification, cancellationToken);
            if (entity == null)
            {
                entity = await base.FirstOrDefaultAsync(specification, cancellationToken);
                if (entity != null)
                {
                    await AddOrUpdateEntitiesAsync([entity], cancellationToken);
                }
            }
            return entity;
        }

        public override async Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<TEntity, TResult> specification, CancellationToken cancellationToken = default) where TResult : default
        {
            var entity = await _inMemoryRepo.FirstOrDefaultAsync(specification, cancellationToken);
            if (entity == null)
            {
                entity = await base.FirstOrDefaultAsync(specification, cancellationToken);
            }
            return entity;
        }

        public override async Task<TEntity?> SingleOrDefaultAsync(ISingleResultSpecification<TEntity> specification, CancellationToken cancellationToken = default)
        {
            var entity = await _inMemoryRepo.SingleOrDefaultAsync(specification, cancellationToken);
            if (entity == null)
            {
                entity = await base.SingleOrDefaultAsync(specification, cancellationToken);
                if (entity != null)
                {
                    await AddOrUpdateEntitiesAsync([entity], cancellationToken);
                }
            }
            return entity;
        }

        public override async Task<TResult?> SingleOrDefaultAsync<TResult>(ISingleResultSpecification<TEntity, TResult> specification, CancellationToken cancellationToken = default) where TResult : default
        {
            var entity = await _inMemoryRepo.SingleOrDefaultAsync(specification, cancellationToken);
            if (entity == null)
            {
                entity = await base.SingleOrDefaultAsync(specification, cancellationToken);
            }
            return entity;
        }

        public override async Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            await base.UpdateAsync(entity, cancellationToken);
            await AddOrUpdateEntitiesAsync([entity], cancellationToken);
        }

        public override async Task UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            await base.UpdateRangeAsync(entities, cancellationToken);
            await AddOrUpdateEntitiesAsync(entities);
        }


        async Task AddOrUpdateEntitiesAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            foreach (var entity in entities)
            {
                if (entity is not RecordBase entityBase)
                {
                    continue;
                }
                var entityFromDb = await _dbContext.FindAsync<TEntity>(entityBase.Id) as RecordBase;
                if (entityFromDb == null)
                {
                    await _inMemoryRepo.AddAsync(entity, cancellationToken);
                }
                else
                {
                    entityFromDb.With(entityBase);
                    await _inMemoryRepo.SaveChangesAsync(cancellationToken);
                }
            }

        }

        public override void Dispose()
        {
            base.Dispose();
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
        }


    }

}
