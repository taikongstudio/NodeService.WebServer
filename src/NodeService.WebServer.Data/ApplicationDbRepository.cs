namespace NodeService.WebServer.Data;

public class ApplicationDbRepository : IApplicationDbRepository, IDisposable
{
    readonly ApplicationDbContext _dbContext;
    readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;

    public ApplicationDbRepository(IDbContextFactory<ApplicationDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
        _dbContext = _dbContextFactory.CreateDbContext();
    }


    public void Dispose()
    {
    }

    public TEntity? Find<TIdentity, TEntity>(TIdentity identity)
        where TIdentity : notnull
        where TEntity : class
    {
        return _dbContext.Find<TEntity>(identity);
    }

    public TEntity? Find<TEntity>(params object[] keyValues) where TEntity : class
    {
        return _dbContext.Find<TEntity>(keyValues);
    }

    public ValueTask<TEntity?> FindAsync<TIdentity, TEntity>(TIdentity identity)
        where TIdentity : notnull
        where TEntity : class
    {
        return _dbContext.FindAsync<TEntity>(identity);
    }

    public ValueTask<TEntity?> FindAsync<TEntity>(params object[] keyValues)
        where TEntity : class
    {
        return _dbContext.FindAsync<TEntity>(keyValues);
    }
}