using Microsoft.Extensions.DependencyInjection;
using System;

namespace NodeService.WebServer.Data.Repositories;

public class InMemoryRepositoryFactory<TEntity> :
    EFRepositoryFactory<IRepository<TEntity>,
        InMemoryRepository<TEntity>,
        InMemoryDbContext>
    where TEntity : class, IAggregateRoot
{
    readonly IDbContextFactory<InMemoryDbContext> _inMemoryDbContextFactory;
    readonly IDbContextFactory<ApplicationDbContext> _applicationDbContextFactory;
    readonly IServiceProvider _serviceProvider;

    public InMemoryRepositoryFactory(
        IServiceProvider serviceProvider,
        IDbContextFactory<InMemoryDbContext> inMemoryDbContextFactory,
                IDbContextFactory<ApplicationDbContext> applicationDbContextFactory) : base(
        inMemoryDbContextFactory)
    {
        _inMemoryDbContextFactory = inMemoryDbContextFactory;
        _applicationDbContextFactory = applicationDbContextFactory;
        _serviceProvider = serviceProvider;
    }

    public new IRepository<TEntity> CreateRepository()
    {
        return CreateRepositoryImpl();
    }

    private IRepository<TEntity> CreateRepositoryImpl()
    {
        var args = new object[]
            {
                                _inMemoryDbContextFactory.CreateDbContext(),
                                _applicationDbContextFactory
            };
        return (IRepository<TEntity>)Activator.CreateInstance(typeof(InMemoryRepository<TEntity>), args)!;
    }




}