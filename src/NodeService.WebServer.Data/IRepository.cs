﻿using Ardalis.Specification;

namespace NodeService.WebServer.Data;

public interface IRepository<T> :
    IRepositoryBase<T>,
    IDisposable,
    IAsyncDisposable
    where T : class, IAggregateRoot
{
    public int LastSaveChangesCount { get; }

    public TimeSpan LastSaveChangesTimeSpan { get; }

    public DbContext DbContext { get; }
}