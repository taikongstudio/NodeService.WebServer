namespace NodeService.WebServer.Data.Repositories
{
    public abstract class SelectSpecification<TEntity, TResult> : Specification<TEntity, TResult?>
        where TEntity : EntityBase
        where TResult : class
    {
        protected SelectSpecification()
        {
            Query.AsNoTrackingWithIdentityResolution();
            Query.Select(x => x as TResult);
        }

        public ListSpecification<TEntity> CreateListSpecification(DataFilterCollection<string> idFilters)
        {
            return new ListSpecification<TEntity>(this, idFilters);
        }

    }
}
