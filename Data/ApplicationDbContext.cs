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

    public DbSet<Warehouse> Warehouses { get; set; }

    public DbSet<Zone> Zones { get; set; }

    public DbSet<Location> Locations { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<UnitOfMeasure>()
            .HasIndex(u => u.Code)
            .IsUnique();

        builder.Entity<Product>()
            .HasIndex(p => p.Code)
            .IsUnique();

        builder.Entity<Warehouse>()
            .HasIndex(w => w.Code)
            .IsUnique();

        builder.Entity<Zone>()
            .HasIndex(z => z.Code)
            .IsUnique();

        builder.Entity<Location>()
            .HasIndex(l => l.Code)
            .IsUnique();

        builder.Entity<Zone>()
            .HasOne(z => z.Warehouse)
            .WithMany(w => w.Zones)
            .HasForeignKey(z => z.WarehouseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Location>()
            .HasOne(l => l.Zone)
            .WithMany(z => z.Locations)
            .HasForeignKey(l => l.ZoneId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
