using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Collections.Concurrent;

namespace NodeService.WebServer.Data.Repositories;

public class ApplicationRepositoryFactory<TEntity> :
    EFRepositoryFactory<IRepository<TEntity>,
        EFRepository<TEntity, ApplicationDbContext>,
        ApplicationDbContext>
    where TEntity : class, IAggregateRoot
{
    readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    readonly SemaphoreSlim _semaphoreSlim;

    public ApplicationRepositoryFactory(IDbContextFactory<ApplicationDbContext> dbContextFactory) : base(
        dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
        _semaphoreSlim = new SemaphoreSlim(30, 50);
    }

    public new IRepository<TEntity> CreateRepository()
    {
        return CreateRepositoryCore(true);
    }

    private IRepository<TEntity> CreateRepositoryCore(bool wait)
    {
        if (wait)
        {
            _semaphoreSlim.Wait();
        }
        var args = new object[] { _dbContextFactory.CreateDbContext(), _semaphoreSlim };
        return (IRepository<TEntity>)Activator.CreateInstance(typeof(SemaphoreEFRepository<TEntity, ApplicationDbContext>), args)!;
    }

    public async ValueTask<IRepository<TEntity>> CreateRepositoryAsync(CancellationToken cancellationToken = default)
    {
        if (!await _semaphoreSlim.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
        {
            var entityTypeName = typeof(TEntity).FullName;
            throw new Exception($"{entityTypeName}:time out ");
        }
        return this.CreateRepositoryCore(false);
    }





}