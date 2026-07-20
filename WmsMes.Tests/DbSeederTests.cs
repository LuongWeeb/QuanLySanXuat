using WmsMes.Web.Data;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Tests;

public class DbSeederTests
{
    [Fact]
    public void ComprehensiveSampleDataSeeder_IsExposed()
    {
        var method = typeof(DbSeeder).GetMethod("SeedComprehensiveSampleDataAsync");

        Assert.NotNull(method);
    }

    [Fact]
    public async Task ComprehensiveSampleDataSeeder_CreatesExpectedScenario_AndIsIdempotent()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new ApplicationDbContext(options);
        await DbSeeder.SeedUnitOfMeasuresAsync(context);
        await DbSeeder.SeedWarehouseStructureAsync(context);

        await DbSeeder.SeedComprehensiveSampleDataAsync(context, null!);
        await DbSeeder.SeedComprehensiveSampleDataAsync(context, null!);

        Assert.Equal(8, await context.Products.CountAsync(p => p.Code.StartsWith("RM-") || p.Code.StartsWith("PROD-")));
        Assert.Equal(2, await context.Suppliers.CountAsync(s => s.Code.StartsWith("SUPP-")));
        Assert.Equal(2, await context.Customers.CountAsync(c => c.Code.StartsWith("CUST-")));
        Assert.Equal(3, await context.WorkCenters.CountAsync(w => w.Code.StartsWith("WC-")));
        Assert.Equal(2, await context.BOMs.CountAsync());
        Assert.Equal(2, await context.Routings.CountAsync());
        Assert.Equal(2, await context.QCChecklists.CountAsync());
        Assert.Equal(2, await context.GoodsReceipts.CountAsync(r => r.ReceiptNo.StartsWith("GR-20260715-")));
        Assert.Single(await context.GoodsIssues.Where(i => i.IssueNo.StartsWith("GI-20260716-")).ToListAsync());
        Assert.Equal(6, await context.WorkOrders.CountAsync(w => w.Code.StartsWith("WO-20260717-")));

        var completed = await context.WorkOrders.SingleAsync(w => w.Code == "WO-20260717-01");
        var inProgress = await context.WorkOrders.SingleAsync(w => w.Code == "WO-20260717-02");
        var pendingQcOrder = await context.WorkOrders.SingleAsync(w => w.Code == "WO-20260717-04");
        var pendingOrder = await context.WorkOrders.SingleAsync(w => w.Code == "WO-20260717-05");
        var approvedOrder = await context.WorkOrders.SingleAsync(w => w.Code == "WO-20260717-06");

        Assert.Equal(WorkOrderStatus.Completed, completed.Status);
        Assert.Equal(WorkOrderStatus.InProgress, inProgress.Status);
        Assert.Equal(WorkOrderStatus.Pending, pendingOrder.Status);
        Assert.Equal(WorkOrderStatus.Approved, approvedOrder.Status);

        Assert.Equal(4, await context.LotGenealogies.CountAsync());
        Assert.Single(await context.QCInspections.Where(i => i.WorkOrderId == completed.Id).ToListAsync());
        Assert.Single(await context.StockBalances.Where(b => b.QtyOnHold > 0).ToListAsync());

        var absBalance = await context.StockBalances
            .Include(b => b.Product)
            .SingleAsync(b => b.Product!.Code == "RM-ABS-01");
        Assert.Equal(460m, absBalance.QtyAvailable);
        Assert.Equal(40m, absBalance.QtyReserved);
    }

    [Fact]
    public async Task FoundationSeeders_CompletePartiallySeededDatabase()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new ApplicationDbContext(options);
        context.UnitOfMeasures.Add(new() { Code = "KG", Name = "Kilogram" });
        context.Warehouses.Add(new() { Code = "WH01", Name = "Kho chính Nhà máy" });
        await context.SaveChangesAsync();

        await DbSeeder.SeedUnitOfMeasuresAsync(context);
        await DbSeeder.SeedWarehouseStructureAsync(context);

        Assert.Equal(4, await context.UnitOfMeasures.CountAsync());
        Assert.Equal(4, await context.Locations.CountAsync(l => l.Code.StartsWith("LOC-")));
    }
}
