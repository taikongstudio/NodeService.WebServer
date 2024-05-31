namespace NodeService.WebServer.Data.Repositories;

public class EfRepository<T> :
    RepositoryBase<T>,
    IReadRepository<T>,
    IRepository<T>,
    IDisposable
    where T : class, IAggregateRoot
{
    private readonly ApplicationDbContext _dbContext;

    public EfRepository(ApplicationDbContext dbContext) : base(dbContext)
    {
        _dbContext = dbContext;
    }

    public int LastChangesCount { get; private set; }

    public DbContext DbContext => _dbContext;

    public TimeSpan LastSaveChangesTimeSpan { get; private set; }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        LastChangesCount = await base.SaveChangesAsync(cancellationToken);
        stopwatch.Stop();
        LastSaveChangesTimeSpan = stopwatch.Elapsed;
        return LastChangesCount;
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