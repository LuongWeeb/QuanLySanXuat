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

    public DbSet<StockTransfer> StockTransfers { get; set; }

    public DbSet<StockTransferLine> StockTransferLines { get; set; }

    public DbSet<Stocktake> Stocktakes { get; set; }

    public DbSet<StocktakeLine> StocktakeLines { get; set; }

    public DbSet<BOM> BOMs { get; set; }

    public DbSet<BOMItem> BOMItems { get; set; }

    public DbSet<WorkCenter> WorkCenters { get; set; }

    public DbSet<Routing> Routings { get; set; }

    public DbSet<RoutingStep> RoutingSteps { get; set; }

    public DbSet<WorkOrder> WorkOrders { get; set; }

    public DbSet<WorkOrderStep> WorkOrderSteps { get; set; }

    public DbSet<MaterialReservation> MaterialReservations { get; set; }

    public DbSet<LotGenealogy> LotGenealogies { get; set; }

    public DbSet<QCChecklist> QCChecklists { get; set; }

    public DbSet<QCChecklistItem> QCChecklistItems { get; set; }

    public DbSet<QCInspection> QCInspections { get; set; }

    public DbSet<QCInspectionLine> QCInspectionLines { get; set; }

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

        builder.Entity<GoodsIssue>()
            .HasOne(issue => issue.Customer)
            .WithMany(customer => customer.GoodsIssues)
            .HasForeignKey(issue => issue.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

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

        builder.Entity<StockTransfer>()
            .HasIndex(t => t.TransferNo)
            .IsUnique();

        builder.Entity<Stocktake>()
            .HasIndex(s => s.StocktakeNo)
            .IsUnique();

        builder.Entity<StockTransferLine>()
            .HasOne(line => line.StockTransfer)
            .WithMany(transfer => transfer.Lines)
            .HasForeignKey(line => line.StockTransferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StockTransferLine>()
            .HasOne(line => line.Product)
            .WithMany()
            .HasForeignKey(line => line.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StockTransferLine>()
            .HasOne(line => line.Lot)
            .WithMany()
            .HasForeignKey(line => line.LotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StockTransferLine>()
            .HasOne(line => line.FromLocation)
            .WithMany()
            .HasForeignKey(line => line.FromLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StockTransferLine>()
            .HasOne(line => line.ToLocation)
            .WithMany()
            .HasForeignKey(line => line.ToLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Stocktake>()
            .HasOne(stocktake => stocktake.Location)
            .WithMany()
            .HasForeignKey(stocktake => stocktake.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StocktakeLine>()
            .HasOne(line => line.Stocktake)
            .WithMany(stocktake => stocktake.Lines)
            .HasForeignKey(line => line.StocktakeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StocktakeLine>()
            .HasOne(line => line.Product)
            .WithMany()
            .HasForeignKey(line => line.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StocktakeLine>()
            .HasOne(line => line.Lot)
            .WithMany()
            .HasForeignKey(line => line.LotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<WorkCenter>()
            .HasIndex(w => w.Code)
            .IsUnique();

        builder.Entity<WorkOrder>()
            .HasIndex(w => w.Code)
            .IsUnique();

        builder.Entity<BOM>()
            .HasMany(b => b.Items)
            .WithOne(i => i.Bom)
            .HasForeignKey(i => i.BomId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<BOMItem>()
            .HasOne(i => i.ComponentProduct)
            .WithMany()
            .HasForeignKey(i => i.ComponentProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Routing>()
            .HasMany(r => r.Steps)
            .WithOne(s => s.Routing)
            .HasForeignKey(s => s.RoutingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<RoutingStep>()
            .HasOne(s => s.WorkCenter)
            .WithMany()
            .HasForeignKey(s => s.WorkCenterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<WorkOrder>()
            .HasMany(w => w.Steps)
            .WithOne(s => s.WorkOrder)
            .HasForeignKey(s => s.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<WorkOrderStep>()
            .HasOne(s => s.WorkCenter)
            .WithMany()
            .HasForeignKey(s => s.WorkCenterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MaterialReservation>()
            .HasOne(r => r.WorkOrder)
            .WithMany()
            .HasForeignKey(r => r.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<MaterialReservation>()
            .HasOne(r => r.Product)
            .WithMany()
            .HasForeignKey(r => r.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MaterialReservation>()
            .HasOne(r => r.Lot)
            .WithMany()
            .HasForeignKey(r => r.LotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<MaterialReservation>()
            .HasOne(r => r.Location)
            .WithMany()
            .HasForeignKey(r => r.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Lot>()
            .HasOne(l => l.WorkOrder)
            .WithMany()
            .HasForeignKey(l => l.WorkOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<LotGenealogy>()
            .HasOne(g => g.OutputLot)
            .WithMany()
            .HasForeignKey(g => g.OutputLotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<LotGenealogy>()
            .HasOne(g => g.InputLot)
            .WithMany()
            .HasForeignKey(g => g.InputLotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<QCChecklist>()
            .HasMany(c => c.Items)
            .WithOne(i => i.QCChecklist)
            .HasForeignKey(i => i.QCChecklistId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<QCInspection>()
            .HasMany(i => i.Lines)
            .WithOne(l => l.QCInspection)
            .HasForeignKey(l => l.QCInspectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<QCChecklist>()
            .HasOne(c => c.Product)
            .WithMany()
            .HasForeignKey(c => c.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<QCInspection>()
            .HasOne(i => i.WorkOrder)
            .WithMany()
            .HasForeignKey(i => i.WorkOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<QCInspection>()
            .HasOne(i => i.Lot)
            .WithMany()
            .HasForeignKey(i => i.LotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<QCInspection>()
            .HasIndex(i => i.LotId)
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
