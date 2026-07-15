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

    public DbSet<Supplier> Suppliers { get; set; }

    public DbSet<Customer> Customers { get; set; }

    public DbSet<Lot> Lots { get; set; }

    public DbSet<StockBalance> StockBalances { get; set; }

    public DbSet<StockTransaction> StockTransactions { get; set; }

    public DbSet<GoodsReceipt> GoodsReceipts { get; set; }

    public DbSet<GoodsReceiptLine> GoodsReceiptLines { get; set; }

    public DbSet<GoodsIssue> GoodsIssues { get; set; }

    public DbSet<GoodsIssueLine> GoodsIssueLines { get; set; }

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

        builder.Entity<Supplier>()
            .HasIndex(s => s.Code)
            .IsUnique();

        builder.Entity<Customer>()
            .HasIndex(c => c.Code)
            .IsUnique();

        builder.Entity<Lot>()
            .HasIndex(l => l.LotNo)
            .IsUnique();

        builder.Entity<StockBalance>()
            .HasIndex(sb => new { sb.ProductId, sb.LotId, sb.LocationId })
            .IsUnique();

        builder.Entity<StockBalance>()
            .HasOne(sb => sb.Product)
            .WithMany()
            .HasForeignKey(sb => sb.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StockBalance>()
            .HasOne(sb => sb.Lot)
            .WithMany()
            .HasForeignKey(sb => sb.LotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StockBalance>()
            .HasOne(sb => sb.Location)
            .WithMany()
            .HasForeignKey(sb => sb.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StockTransaction>()
            .HasOne(st => st.Product)
            .WithMany()
            .HasForeignKey(st => st.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StockTransaction>()
            .HasOne(st => st.Lot)
            .WithMany()
            .HasForeignKey(st => st.LotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StockTransaction>()
            .HasOne(st => st.Location)
            .WithMany()
            .HasForeignKey(st => st.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<GoodsReceipt>()
            .HasIndex(r => r.ReceiptNo)
            .IsUnique();

        builder.Entity<GoodsIssue>()
            .HasIndex(i => i.IssueNo)
            .IsUnique();

        builder.Entity<GoodsReceiptLine>()
            .HasOne(line => line.GoodsReceipt)
            .WithMany(receipt => receipt.Lines)
            .HasForeignKey(line => line.GoodsReceiptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<GoodsReceiptLine>()
            .HasOne(line => line.Product)
            .WithMany()
            .HasForeignKey(line => line.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<GoodsReceiptLine>()
            .HasOne(line => line.Location)
            .WithMany()
            .HasForeignKey(line => line.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<GoodsIssueLine>()
            .HasOne(line => line.GoodsIssue)
            .WithMany(issue => issue.Lines)
            .HasForeignKey(line => line.GoodsIssueId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<GoodsIssueLine>()
            .HasOne(line => line.Product)
            .WithMany()
            .HasForeignKey(line => line.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<GoodsIssueLine>()
            .HasOne(line => line.Lot)
            .WithMany()
            .HasForeignKey(line => line.LotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<GoodsIssueLine>()
            .HasOne(line => line.Location)
            .WithMany()
            .HasForeignKey(line => line.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

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
