using Ardalis.Specification;
using Ardalis.Specification.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data.Repositories
{
    public class EfRepository<T> :
        RepositoryBase<T>,
        IReadRepository<T>,
        IRepository<T>,
        IDisposable
        where T : class, IAggregateRoot
    {
        private readonly ApplicationDbContext _dbContext;

        public EfRepository(ApplicationDbContext dbContext) : base(dbContext)
        {
            _dbContext = dbContext;
        }

        public int LastSaveChangesCount { get; private set; }

        public DbContext DbContext => _dbContext;

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            LastSaveChangesCount = await base.SaveChangesAsync(cancellationToken);
            return LastSaveChangesCount;
        }


        public void Dispose()
        {
            _dbContext.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            return _dbContext.DisposeAsync();
        }

    }
}
