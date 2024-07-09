



using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore;
using NodeService.Infrastructure.DataModels;

namespace NodeService.WebServer.Data.Repositories;

public class EfRepository<T> :
    RepositoryBase<T>,
    IReadRepository<T>,
    IRepository<T>,
    IDisposable
    where T : class, IAggregateRoot
{
    readonly ApplicationDbContext _dbContext;

    readonly Stopwatch _stopwatch;


    public EfRepository(ApplicationDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
        _stopwatch = new Stopwatch();
    }


    public int LastSaveChangesCount { get; private set; }

    public DbContext DbContext => _dbContext;

    public TimeSpan LastOperationTimeSpan { get; private set; }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var entries = this.DbContext.ChangeTracker.Entries<JsonBasedDataModel>();
        foreach (var entry in entries)
        {
            if (entry.Entity is not T value)
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

    private void DbContext_SavingChanges(object? sender, SavingChangesEventArgs e)
    {
        
    }

    public override Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return base.CountAsync(cancellationToken);
    }

    public override Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        OnEntityUpdate(entity);
        return base.UpdateAsync(entity, cancellationToken);
    }

    private void OnEntityUpdate(T entity)
    {
        if (entity is JsonBasedDataModel jsonBasedDataModel)
        {
            if (jsonBasedDataModel.CreationDateTime == default)
            {
                jsonBasedDataModel.CreationDateTime = DateTime.UtcNow;
            }
            if (jsonBasedDataModel.ModifiedDateTime == default)
            {
                jsonBasedDataModel.ModifiedDateTime = DateTime.UtcNow;
            }
        }
    }

    public override Task UpdateRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        foreach (var item in entities)
        {
            OnEntityUpdate(item);
        }
        return base.UpdateRangeAsync(entities, cancellationToken);
    }

    public override Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        OnEntityAdd(entity);
        return base.AddAsync(entity, cancellationToken);
    }

    private void OnEntityAdd(T entity)
    {
        if (entity is JsonBasedDataModel jsonBasedDataModel)
        {
            if (jsonBasedDataModel.CreationDateTime == default)
            {
                jsonBasedDataModel.CreationDateTime = DateTime.UtcNow;
            }
            jsonBasedDataModel.ModifiedDateTime = DateTime.UtcNow;
        }
    }

    public override Task<IEnumerable<T>> AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        foreach (var entity in entities)
        {
            OnEntityAdd(entity);
        }
        return base.AddRangeAsync(entities, cancellationToken);
    }

    public override async Task<List<T>> ListAsync(CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var list = await base.ListAsync(cancellationToken);
        _stopwatch.Stop();
        LastOperationTimeSpan = _stopwatch.Elapsed;
        return list;
    }

    public override async Task<List<T>> ListAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var list = await base.ListAsync(specification, cancellationToken);
        _stopwatch.Stop();
        LastOperationTimeSpan = _stopwatch.Elapsed;
        return list;
    }

    public override async Task<List<TResult>> ListAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var list = await base.ListAsync(specification, cancellationToken);
        _stopwatch.Stop();
        LastOperationTimeSpan = _stopwatch.Elapsed;
        return list;
    }

    public override async Task<T?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var item = await base.GetByIdAsync(id, cancellationToken);
        _stopwatch.Stop();
        LastOperationTimeSpan = _stopwatch.Elapsed;
        return item;
    }

    public override async Task<int> CountAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var value = await base.CountAsync(specification, cancellationToken);
        _stopwatch.Stop();
        LastOperationTimeSpan = _stopwatch.Elapsed;
        return value;
    }

    public override async Task<T?> FirstOrDefaultAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var item = await base.FirstOrDefaultAsync(specification, cancellationToken);
        _stopwatch.Stop();
        LastOperationTimeSpan = _stopwatch.Elapsed;
        return item;
    }

    public override async Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<T, TResult> specification, CancellationToken cancellationToken = default) where TResult : default
    {
        _stopwatch.Restart();
        var item = await base.FirstOrDefaultAsync(specification, cancellationToken);
        _stopwatch.Stop();
        LastOperationTimeSpan = _stopwatch.Elapsed;
        return item;
    }

    public override async Task<T?> SingleOrDefaultAsync(ISingleResultSpecification<T> specification, CancellationToken cancellationToken = default)
    {
        _stopwatch.Restart();
        var item = await base.SingleOrDefaultAsync(specification, cancellationToken);
        _stopwatch.Stop();
        LastOperationTimeSpan = _stopwatch.Elapsed;
        return item;
    }

    public override async Task<TResult?> SingleOrDefaultAsync<TResult>(ISingleResultSpecification<T, TResult> specification, CancellationToken cancellationToken = default) where TResult : default
    {
        _stopwatch.Restart();
        var item = await base.SingleOrDefaultAsync(specification, cancellationToken);
        _stopwatch.Stop();
        LastOperationTimeSpan = _stopwatch.Elapsed;
        return item;
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