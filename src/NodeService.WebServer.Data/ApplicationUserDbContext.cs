using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Debug;
using NodeService.Infrastructure.Identity;

namespace NodeService.WebServer.Data;

public class ApplicationUserDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string,
    IdentityUserClaim<string>
    , ApplicationUserRole, IdentityUserLogin<string>, IdentityRoleClaim<string>, IdentityUserToken<string>>
{
    public static readonly LoggerFactory DebugLoggerFactory = new(new[]
    {
        new DebugLoggerProvider()
    });

    public ApplicationUserDbContext(DbContextOptions<ApplicationUserDbContext> options) : base(options)
    {
    }


    protected ApplicationUserDbContext(DbContextOptions contextOptions)
        : base(contextOptions)
    {
    }

    public DbSet<ApplicationUserRole> ApplicationUserRolesDbSet { get; set; }

    public DbSet<ApplicationUser> ApplicationUsersDbSet { get; set; }

    public DbSet<ApplicationRole> ApplicationRolesDbSet { get; set; }

    public DbSet<IdentityRoleClaim<string>> IdentityRoleClaimsDbSet { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ApplicationUserRole>(userRole =>
        {
            userRole.HasKey(ur => new { ur.UserId, ur.RoleId });
            //userRole.HasOne(ur => ur.Role)
            //    .WithMany(r => r.UserRoles)
            //    .HasForeignKey(ur => ur.RoleId).IsRequired();

            //userRole.HasOne(ur => ur.User)
            //    .WithMany(r => r.UserRoles)
            //    .HasForeignKey(ur => ur.UserId).IsRequired();
        });
        modelBuilder.Entity<ApplicationRole>(entity =>
        {
            entity.HasData(
                new ApplicationRole("user")
                {
                    Id = "03B11179-8A33-4D3B-8092-463249F755A5",
                    NormalizedName = "USER",
                    ConcurrencyStamp = "ABCD1254-FEE2-42D0-A96E-E2018C9161BF"
                }, new ApplicationRole("admin")
                {
                    Id = "CF0B61E1-3BB2-40D6-8E17-60CF475CE884",
                    NormalizedName = "ADMIN",
                    ConcurrencyStamp = "D5FC26A2-75FD-45F8-AB88-DF4D337C5910"
                });
        });
        modelBuilder.Entity<IdentityRoleClaim<string>>(entity =>
        {
            entity.HasKey(nameof(IdentityRoleClaim<string>.Id));
            entity.HasData(
                new IdentityRoleClaim<string>
                {
                    RoleId = "03B11179-8A33-4D3B-8092-463249F755A5",
                    Id = 1,
                    ClaimType = "Permission",
                    ClaimValue = "Administrative.ViewUsers"
                }, new IdentityRoleClaim<string>
                {
                    RoleId = "CF0B61E1-3BB2-40D6-8E17-60CF475CE884",
                    Id = 2,
                    ClaimType = "Permission",
                    ClaimValue = "Administrative.ViewUsers"
                }, new IdentityRoleClaim<string>
                {
                    RoleId = "CF0B61E1-3BB2-40D6-8E17-60CF475CE884",
                    Id = 3,
                    ClaimType = "Permission",
                    ClaimValue = "Administrative.ManageUsers"
                });
        });
        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            var data = new ApplicationUser
            {
                Email = "follow@live.in",
                ConcurrencyStamp = "2fe7f8d5-d321-4c77-884b-73ea438b1511",
                LockoutEnabled = true,
                Id = "c784d6e7-4424-4fe1-a1bb-b03c6a9a26cb",
                NormalizedEmail = "FOLLOW@LIVE.IN",
                PhoneNumber = "09127372975",
                EmailConfirmed = true,
                Name = "admin",
                Family = "admin",
                RegisterDate = DateTime.UtcNow,
                SecurityStamp = "LJNTPIYBD4KN2CFESBRMRL2YDQOXANQ4",
                UserName = "follow@live.in",
                NormalizedUserName = "FOLLOW@LIVE.IN",
                PhoneNumberConfirmed = true,
                RefreshTokenExpireTime = DateTime.MinValue,
                PasswordHash = "AQAAAAIAAYagAAAAEK1W3FMebsaQ5p6sqwXybnO6AdMcllqC99NBccKaS99FJZji0MmRjLfY4vMAR/ldRA=="
            };
            var s = new PasswordHasher<ApplicationUser>();
            var hash = s.HashPassword(data, "xwdadmin123");
            data.PasswordHash = hash;

            entity.HasData(data
                ,
                new ApplicationUser
                {
                    Email = "ashrafi.milad@gmail.com",
                    ConcurrencyStamp = "6b263a8b-120f-4f48-a6bd-ad3a9c4c913d",
                    LockoutEnabled = true,
                    Id = "f0dccee8-a3e1-45f8-9bb7-f7e7decebd09",
                    NormalizedEmail = "ASHRAFI.MILAD@GMAIL.COM",
                    PhoneNumber = "09127372975",
                    EmailConfirmed = true,
                    Name = "Milad",
                    Family = "Ashrafi (user)",
                    RegisterDate = DateTime.UtcNow,
                    SecurityStamp = "OHACRUB556PUCIJOKNPX6QMTHA5G77DG",
                    UserName = "ashrafi.milad@gmail.com",
                    NormalizedUserName = "ASHRAFI.MILAD@GMAIL.COM",
                    PhoneNumberConfirmed = true,
                    RefreshTokenExpireTime = DateTime.MinValue,
                    PasswordHash =
                        "AQAAAAIAAYagAAAAEK1W3FMebsaQ5p6sqwXybnO6AdMcllqC99NBccKaS99FJZji0MmRjLfY4vMAR/ldRA=="
                });
        });

        modelBuilder.Entity<ApplicationUserRole>(entity =>
        {
            entity.HasData(new ApplicationUserRole
            {
                UserId = "c784d6e7-4424-4fe1-a1bb-b03c6a9a26cb",
                RoleId = "CF0B61E1-3BB2-40D6-8E17-60CF475CE884"
            }, new ApplicationUserRole
            {
                UserId = "f0dccee8-a3e1-45f8-9bb7-f7e7decebd09",
                RoleId = "03B11179-8A33-4D3B-8092-463249F755A5"
            });
        });
    }
}