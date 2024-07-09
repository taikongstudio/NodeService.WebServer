



using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore;
using NodeService.Infrastructure.DataModels;

namespace NodeService.WebServer.Data.Repositories;

public class EfRepository<TEntity, TDbContext> :
    RepositoryBase<TEntity>,
    IReadRepository<TEntity>,
    IRepository<TEntity>,
    IDisposable
    where TEntity : class, IAggregateRoot
    where TDbContext : DbContext
{
    readonly DbContext _dbContext;

    readonly Stopwatch _stopwatch;


    public EfRepository(TDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
        _stopwatch = new Stopwatch();
    }

    

    public int LastSaveChangesCount { get; private set; }

    public DbContext DbContext => _dbContext;

    public TimeSpan TimeSpan { get; private set; }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var entries = this.DbContext.ChangeTracker.Entries<JsonRecordBase>();
        foreach (var entry in entries)
        {
            if (entry.Entity is not TEntity value)
            {
                continue;
            }
            OnEntityUpdate(value);
        }
        LastSaveChangesCount = await base.SaveChangesAsync(cancellationToken);
        _stopwatch.Stop();
        TimeSpan = _stopwatch.Elapsed;
        return LastSaveChangesCount;
    }

    public override Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return base.CountAsync(cancellationToken);
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

    public override Task UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        foreach (var entity in entities)
        {
            OnEntityUpdate(entity);
        }
        return base.UpdateRangeAsync(entities, cancellationToken);
    }

    public override async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        OnEntityAdd(entity);
        _stopwatch.Restart();
        var value = await base.AddAsync(entity, cancellationToken);
        _stopwatch.Stop();
        TimeSpan = _stopwatch.Elapsed;
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

    public override Task<IEnumerable<TEntity>> AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
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
        _stopwatch.Stop();
        TimeSpan = _stopwatch.Elapsed;
        return list;
    }

    public override async Task<List<TEntity>> ListAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var list = await base.ListAsync(specification, cancellationToken);
        _stopwatch.Stop();
        TimeSpan = _stopwatch.Elapsed;
        return list;
    }

    public override async Task<List<TResult>> ListAsync<TResult>(ISpecification<TEntity, TResult> specification, CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var list = await base.ListAsync(specification, cancellationToken);
        _stopwatch.Stop();
        TimeSpan = _stopwatch.Elapsed;
        return list;
    }

    public override async Task<TEntity?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var entity = await base.GetByIdAsync(id, cancellationToken);
        _stopwatch.Stop();
        TimeSpan = _stopwatch.Elapsed;
        return entity;
    }

    public override async Task<int> CountAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var entity = await base.CountAsync(specification, cancellationToken);
        _stopwatch.Stop();
        TimeSpan = _stopwatch.Elapsed;
        return entity;
    }

    public override async Task<TEntity?> FirstOrDefaultAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var entity = await base.FirstOrDefaultAsync(specification, cancellationToken);
        _stopwatch.Stop();
        TimeSpan = _stopwatch.Elapsed;
        return entity;
    }

    public override async Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<TEntity, TResult> specification, CancellationToken cancellationToken = default) where TResult : default
    {
        _stopwatch.Restart();
        var entity = await base.FirstOrDefaultAsync(specification, cancellationToken);
        _stopwatch.Stop();
        TimeSpan = _stopwatch.Elapsed;
        return entity;
    }

    public override async Task<TEntity?> SingleOrDefaultAsync(ISingleResultSpecification<TEntity> specification, CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var entity = await base.SingleOrDefaultAsync(specification, cancellationToken);
        _stopwatch.Stop();
        TimeSpan = _stopwatch.Elapsed;
        return entity;
    }

    public override async Task<TResult?> SingleOrDefaultAsync<TResult>(ISingleResultSpecification<TEntity, TResult> specification, CancellationToken cancellationToken = default) where TResult : default
    {
        _stopwatch.Restart();
        var entity = await base.SingleOrDefaultAsync(specification, cancellationToken);
        _stopwatch.Stop();
        TimeSpan = _stopwatch.Elapsed;
        return entity;
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        return _dbContext.DisposeAsync();
    }
}