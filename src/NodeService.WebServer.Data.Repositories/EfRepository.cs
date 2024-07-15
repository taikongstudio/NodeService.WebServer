using Ardalis.Specification;
using System.Collections.Generic;
using static Grpc.Core.Metadata;

namespace NodeService.WebServer.Data.Repositories;

public class EFRepository<TEntity, TDbContext> :
    RepositoryBase<TEntity>,
    IReadRepository<TEntity>,
    IRepository<TEntity>,
    IDisposable
    where TEntity : class, IAggregateRoot
    where TDbContext : DbContext
{
    readonly TDbContext _dbContext;

    readonly Stopwatch _stopwatch;


    public EFRepository(TDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
        _stopwatch = new Stopwatch();
    }

    

    public int LastSaveChangesCount { get; private set; }

    public TDbContext DbContext => _dbContext;

    public TimeSpan LastOperationTimeSpan { get; private set; }

    DbContext IRepository<TEntity>.DbContext => _dbContext;

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var entries = this.DbContext.ChangeTracker.Entries<JsonRecordBase>();
        foreach (var entry in entries)
        {
            switch (entry.State)
            {
                case EntityState.Modified:
                case EntityState.Added:
                    break;
                case EntityState.Detached:
                case EntityState.Unchanged:
                case EntityState.Deleted:
                default:
                    continue;
            }
            if (entry.Entity is not TEntity value)
            {
                continue;
            }
            OnEntityUpdate(value);
        }
        LastSaveChangesCount = await base.SaveChangesAsync(cancellationToken);
        _stopwatch.Stop();
        LastOperationTimeSpan = _stopwatch.Elapsed;
        return LastSaveChangesCount;
    }

    public override async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var count = await base.CountAsync(cancellationToken);
        _stopwatch.Stop();
        return count;
    }

    public override Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        OnEntityUpdate(entity);
        return base.UpdateAsync(entity, cancellationToken);
    }

    void OnEntityUpdate(TEntity entity)
    {
        if (entity is RecordBase record)
        {
            if (record.CreationDateTime == default)
            {
                record.CreationDateTime = DateTime.UtcNow;
            }
            if (record.ModifiedDateTime == default)
            {
                record.ModifiedDateTime = DateTime.UtcNow;
            }
        }
    }

    public override Task UpdateRangeAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default)
    {
        foreach (var entity in entities)
        {
            OnEntityUpdate(entity);
        }
        return base.UpdateRangeAsync(entities, cancellationToken);
    }

    public override async Task<TEntity> AddAsync(
        TEntity entity,
        CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        OnEntityAdd(entity);
        var value = await base.AddAsync(entity, cancellationToken);
        _stopwatch.Stop();
        LastOperationTimeSpan = _stopwatch.Elapsed;
        return value;
    }

    void OnEntityAdd(TEntity entity)
    {
        if (entity is RecordBase record)
        {
            if (record.CreationDateTime == default)
            {
                record.CreationDateTime = DateTime.UtcNow;
            }
            if (record.ModifiedDateTime == default)
            {
                record.ModifiedDateTime = DateTime.UtcNow;
            }
        }
    }

    public override Task<IEnumerable<TEntity>> AddRangeAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default)
    {
        foreach (var entity in entities)
        {
            OnEntityAdd(entity);
        }
        return base.AddRangeAsync(entities, cancellationToken);
    }

    public override async Task<List<TEntity>> ListAsync(CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var list = await base.ListAsync(cancellationToken);
        SetEntitySource(list);

        _stopwatch.Stop();
        LastOperationTimeSpan = _stopwatch.Elapsed;
        return list;
    }

    void SetEntitySource(params TEntity[] list)
    {
        if (list != null)
        {
            foreach (var item in list)
            {
                if (item is EntityBase entity)
                {
                    entity.EntitySource = EntitySource.Database;
                }
            }
        }
    }

    void SetEntitySource(IEnumerable<TEntity>? list)
    {
        if (list != null)
        {
            foreach (var item in list)
            {
                if (item is EntityBase entity)
                {
                    entity.EntitySource = EntitySource.Database;
                }
            }
        }
    }

    public override async Task<List<TEntity>> ListAsync(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var list = await base.ListAsync(specification, cancellationToken);
        SetEntitySource(list);
        _stopwatch.Stop();
        LastOperationTimeSpan = _stopwatch.Elapsed;
        return list;
    }

    public override async Task<List<TResult>> ListAsync<TResult>(
        ISpecification<TEntity, TResult> specification,
        CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var list = await base.ListAsync(specification, cancellationToken);
        _stopwatch.Stop();
        LastOperationTimeSpan = _stopwatch.Elapsed;
        return list;
    }

    public override async Task<TEntity?> GetByIdAsync<TId>(
        TId id,
        CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var entity = await base.GetByIdAsync(id, cancellationToken);
        SetEntitySource(entity);
        _stopwatch.Stop();
        LastOperationTimeSpan = _stopwatch.Elapsed;
        return entity;
    }

    public override async Task<int> CountAsync(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var count = await base.CountAsync(specification, cancellationToken);
        _stopwatch.Stop();
        LastOperationTimeSpan = _stopwatch.Elapsed;
        return count;
    }

    public override async Task<TEntity?> FirstOrDefaultAsync(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var entity = await base.FirstOrDefaultAsync(specification, cancellationToken);
        SetEntitySource(entity);
        _stopwatch.Stop();
        LastOperationTimeSpan = _stopwatch.Elapsed;
        return entity;
    }

    public override async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var result = await base.AnyAsync(cancellationToken);
        _stopwatch.Stop();
        LastOperationTimeSpan = _stopwatch.Elapsed;
        return result;
    }

    public override async Task DeleteAsync(
        TEntity entity,
        CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        await base.DeleteAsync(entity, cancellationToken);
        _stopwatch.Stop();
    }

    public override async Task DeleteRangeAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        await base.DeleteRangeAsync(
            entities,
            cancellationToken);
        _stopwatch.Stop();
    }

    public override async Task DeleteRangeAsync(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        await base.DeleteRangeAsync(
            specification,
            cancellationToken);
        _stopwatch.Stop();
    }

    public override async Task<bool> AnyAsync(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var value = await base.AnyAsync(
            specification,
            cancellationToken);
        _stopwatch.Stop();
        return value;
    }

    public override async Task<TResult?> FirstOrDefaultAsync<TResult>(
        ISpecification<TEntity, TResult> specification,
        CancellationToken cancellationToken = default) where TResult : default
    {
        _stopwatch.Restart();
        var entity = await base.FirstOrDefaultAsync(
            specification,
            cancellationToken);
        _stopwatch.Stop();
        LastOperationTimeSpan = _stopwatch.Elapsed;
        return entity;
    }

    public override async Task<TEntity?> SingleOrDefaultAsync(
        ISingleResultSpecification<TEntity> specification,
        CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var entity = await base.SingleOrDefaultAsync(
            specification,
            cancellationToken);
        SetEntitySource(entity);
        _stopwatch.Stop();
        LastOperationTimeSpan = _stopwatch.Elapsed;
        return entity;
    }

    public override async Task<TResult?> SingleOrDefaultAsync<TResult>(
        ISingleResultSpecification<TEntity, TResult> specification,
        CancellationToken cancellationToken = default) where TResult : default
    {
        _stopwatch.Restart();
        var entity = await base.SingleOrDefaultAsync(
            specification,
            cancellationToken);
        _stopwatch.Stop();
        LastOperationTimeSpan = _stopwatch.Elapsed;
        return entity;
    }

    public virtual void Dispose()
    {
        _dbContext.Dispose();
    }

    public virtual ValueTask DisposeAsync()
    {
        return _dbContext.DisposeAsync();
    }
}