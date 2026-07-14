using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Domain.Entities;

namespace WmsMes.Web.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<UnitOfMeasure> UnitOfMeasures { get; set; }

    public DbSet<Product> Products { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<UnitOfMeasure>()
            .HasIndex(u => u.Code)
            .IsUnique();

        builder.Entity<Product>()
            .HasIndex(p => p.Code)
            .IsUnique();
    }
}
