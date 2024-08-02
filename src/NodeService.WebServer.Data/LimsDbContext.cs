using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Debug;

namespace NodeService.WebServer.Data
{
    public class LimsDbContext : DbContext
    {
        public static readonly LoggerFactory DebugLoggerFactory = new(
        [
            new DebugLoggerProvider()
        ]);

        public DbSet<dl_common_area> dl_common_area { get; set; }

        public DbSet<dl_equipment_ctrl_computer> dl_equipment_ctrl_computer { get; set; }

        public LimsDbContext(DbContextOptions<LimsDbContext> options) : base(options)
        {

        }


        protected LimsDbContext(DbContextOptions contextOptions)
            : base(contextOptions)
        {

        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<dl_common_area>().HasKey(t => t.id);
            modelBuilder.Entity<dl_equipment_ctrl_computer>().HasKey(t => t.id);
        }

    }
}
