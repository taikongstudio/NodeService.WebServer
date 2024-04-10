using Microsoft.EntityFrameworkCore.Design;

namespace NodeService.WebServer.Data
{
    public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            optionsBuilder.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=JobsWorkerWebServiceTestDb",

                (optionsBuilder) =>
                {
                    optionsBuilder.EnableRetryOnFailure();
                    optionsBuilder.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                });

            return new ApplicationDbContext(optionsBuilder.Options);
        }
    }
}
