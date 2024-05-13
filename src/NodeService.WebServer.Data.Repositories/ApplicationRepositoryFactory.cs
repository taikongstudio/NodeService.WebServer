namespace NodeService.WebServer.Data.Repositories
{
    public class ApplicationRepositoryFactory<T> :
        EFRepositoryFactory<IRepository<T>,
        EfRepository<T>,
        ApplicationDbContext>
        where T : class, IAggregateRoot
    {
        public ApplicationRepositoryFactory(IDbContextFactory<ApplicationDbContext> dbContextFactory) : base(dbContextFactory)
        {
        }
    }
}
