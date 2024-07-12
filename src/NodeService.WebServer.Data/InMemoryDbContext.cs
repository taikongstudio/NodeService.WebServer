using Microsoft.EntityFrameworkCore.Sqlite.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal;
using NodeService.WebServer.Data.Entities;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Data
{
    public class InMemoryDbContext : DbContext
    {
        public InMemoryDbContext(DbContextOptions<InMemoryDbContext> options) : base(options)
        {

        }


        protected InMemoryDbContext(DbContextOptions contextOptions)
            : base(contextOptions)
        {
        }

        public DbSet<NodeFileHittestResultCache> NodeFileHittestResultCacheDbSet { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<NodeFileHittestResultCache>(builder =>
            {
                builder.HasKey(x => new { x.NodeInfoId, x.FullName });
            });
        }
    }
}
