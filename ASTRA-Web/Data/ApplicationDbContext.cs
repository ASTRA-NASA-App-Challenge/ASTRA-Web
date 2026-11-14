using Entities.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;


namespace Data
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            // Identity exposes these framework properties, but ASTRA does not persist or use them.
            builder.Entity<ApplicationUser>().Ignore(user => user.Email);
            builder.Entity<ApplicationUser>().Ignore(user => user.NormalizedEmail);
            builder.Entity<ApplicationUser>().Ignore(user => user.EmailConfirmed);
            builder.Entity<ApplicationUser>().Ignore(user => user.PhoneNumber);
            builder.Entity<ApplicationUser>().Ignore(user => user.PhoneNumberConfirmed);
            builder.Entity<ApplicationUser>().Ignore(user => user.TwoFactorEnabled);
            builder.Entity<ApplicationUser>().HasIndex(user => user.PinLookup).IsUnique();
        }
    }
}
