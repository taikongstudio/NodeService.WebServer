
namespace NodeService.WebServer.Data
{
    public class ApplicationDbRepository : IApplicationDbRepository,IDisposable
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ApplicationDbContext _dbContext;

        public ApplicationDbRepository(IDbContextFactory<ApplicationDbContext> dbContextFactory)
        {
            this._dbContextFactory = dbContextFactory;
            this._dbContext = this._dbContextFactory.CreateDbContext();
        }


        public void Dispose()
        {

        }

        public TEntity? Find<TIdentity, TEntity>(TIdentity identity)
            where TIdentity : notnull
            where TEntity : class
        {
            return this._dbContext.Find<TEntity>(identity);
        }

        public TEntity? Find<TEntity>(params object[] keyValues) where TEntity : class
        {
            return this._dbContext.Find<TEntity>(keyValues);
        }

        public ValueTask<TEntity?> FindAsync<TIdentity, TEntity>(TIdentity identity)
            where TIdentity : notnull
            where TEntity : class
        {
            return this._dbContext.FindAsync<TEntity>(identity);
        }

        public ValueTask<TEntity?> FindAsync<TEntity>(params object[] keyValues)
            where TEntity : class
        {
            return this._dbContext.FindAsync<TEntity>(keyValues);
        }
    }
}
