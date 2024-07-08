



using Google.Protobuf.WellKnownTypes;

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
        LastSaveChangesCount = await base.SaveChangesAsync(cancellationToken);
        _stopwatch.Stop();
        LastOperationTimeSpan = _stopwatch.Elapsed;
        return LastSaveChangesCount;
    }

    public override Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return base.CountAsync(cancellationToken);
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