using Microsoft.EntityFrameworkCore.Infrastructure;

namespace NodeService.WebServer.Data.Repositories;

public class ApplicationRepositoryFactory<TEntity> :
    EFRepositoryFactory<IRepository<TEntity>,
        EFRepository<TEntity, ApplicationDbContext>,
        ApplicationDbContext>
    where TEntity : class, IAggregateRoot
{
    public ApplicationRepositoryFactory(IDbContextFactory<ApplicationDbContext> dbContextFactory) : base(
        dbContextFactory)
    {
    }

  



}