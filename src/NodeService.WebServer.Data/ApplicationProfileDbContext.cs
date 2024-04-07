namespace NodeService.WebServer.Data
{
    public class ApplicationProfileDbContext : DbContext
    {
        public DbSet<MachineInfo> MachineInfoDbSet { get; set; }


        public ApplicationProfileDbContext(DbContextOptions<ApplicationProfileDbContext> contextOptions)
            : base(contextOptions)
        {

        }

        protected ApplicationProfileDbContext(DbContextOptions contextOptions)
            : base(contextOptions)
        {

        }
    }
}
