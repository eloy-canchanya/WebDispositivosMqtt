using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace WebDispositivosMqtt.Identity
{
    public class IdentityAppDbContext : IdentityDbContext<ApplicationUser>
    {
        public IdentityAppDbContext(DbContextOptions<IdentityAppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<ApplicationUser>().ToTable("AspNetUsers", "identity");
            builder.Entity<IdentityRole>().ToTable("AspNetRoles", "identity");
            builder.Entity<IdentityUserRole<string>>().ToTable("AspNetUserRoles", "identity");
            builder.Entity<IdentityUserClaim<string>>().ToTable("AspNetUserClaims", "identity");
            builder.Entity<IdentityUserLogin<string>>().ToTable("AspNetUserLogins", "identity");
            builder.Entity<IdentityRoleClaim<string>>().ToTable("AspNetRoleClaims", "identity");
            builder.Entity<IdentityUserToken<string>>().ToTable("AspNetUserTokens", "identity");
        }
    }
}
