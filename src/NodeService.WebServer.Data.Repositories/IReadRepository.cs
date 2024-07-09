namespace NodeService.WebServer.Data.Repositories;

public interface IReadRepository<T> : IReadRepositoryBase<T> where T : class, IAggregateRoot
{
  
}