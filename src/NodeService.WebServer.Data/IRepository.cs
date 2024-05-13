using Ardalis.Specification;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data
{
    public interface IRepository<T> :
        IRepositoryBase<T>,
        IDisposable,
        IAsyncDisposable
        where T : class, IAggregateRoot
    {
        public int LastSaveChangesCount { get; }

        public DbContext DbContext { get; }
    }
}
