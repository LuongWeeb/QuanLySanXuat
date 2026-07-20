using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.DTOs;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public class CycleCountTests
{
    [Fact]
    public async Task CreateCycleCountOrderAsync_SnapshotsSystemQuantityForEveryBalanceInWarehouse()
    {
        await using var context = CreateContext();
        var (warehouse, balance) = await AddWarehouseBalanceAsync(context, qtyAvailable: 42);
        var otherWarehouse = new Warehouse { Code = "WH-OTHER", Name = "Other warehouse" };
        var otherZone = new Zone { Code = "Z-OTHER", Name = "Other zone", Warehouse = otherWarehouse };
        var otherLocation = new Location { Code = "LOC-OTHER", Name = "Other location", Zone = otherZone };
        context.StockBalances.Add(new StockBalance
        {
            Product = new Product { Code = "P-OTHER", Name = "Other product" },
            Lot = new Lot { LotNo = "LOT-OTHER" },
            Location = otherLocation,
            QtyAvailable = 17
        });
        await context.SaveChangesAsync();

        var order = await new CycleCountService(context, new InventoryService(context))
            .CreateCycleCountOrderAsync(warehouse.Id, "counter-1");

        var item = Assert.Single(order.Items);
        Assert.Equal(balance.ProductId, item.ProductId);
        Assert.Equal(balance.LocationId, item.LocationId);
        Assert.Equal(balance.LotId, item.LotId);
        Assert.Equal(42, item.SystemQty);
        Assert.Null(item.CountedQty);
        Assert.Equal("Draft", order.Status);
        Assert.Equal("counter-1", order.CreatedBy);
        Assert.NotEmpty(order.CountNumber);
    }

    [Fact]
    public async Task ApproveAndAdjustStockAsync_AdjustsBalanceAndCreatesAdjustmentTransactionForVariance()
    {
        await using var context = CreateContext();
        var (warehouse, balance) = await AddWarehouseBalanceAsync(context, qtyAvailable: 10);
        var inventoryService = new InventoryService(context);
        var cycleCountService = new CycleCountService(context, inventoryService);
        var order = await cycleCountService.CreateCycleCountOrderAsync(warehouse.Id, "counter-1");
        var item = Assert.Single(order.Items);

        await cycleCountService.RecordCountResultsAsync(order.Id,
        [
            new CountResultDto { CycleCountItemId = item.Id, CountedQty = 7 }
        ]);

        var approved = await cycleCountService.ApproveAndAdjustStockAsync(order.Id, "approver-1");

        Assert.True(approved);
        Assert.Equal(7, await context.StockBalances.Where(stock => stock.Id == balance.Id).Select(stock => stock.QtyAvailable).SingleAsync());
        var transaction = Assert.Single(await context.StockTransactions.ToListAsync());
        Assert.Equal(TransactionType.Adjust, transaction.Type);
        Assert.Equal(-3, transaction.Qty);
        Assert.Equal(balance.ProductId, transaction.ProductId);
        Assert.Equal(balance.LotId, transaction.LotId);
        Assert.Equal(balance.LocationId, transaction.LocationId);
        Assert.Equal("approver-1", transaction.UserId);
        Assert.Equal(order.CountNumber, transaction.ReferenceNo);
        var persistedOrder = await context.CycleCountOrders.SingleAsync();
        Assert.Equal("Approved", persistedOrder.Status);
        Assert.Equal("approver-1", persistedOrder.ApprovedBy);
        Assert.NotNull(persistedOrder.CompletedAt);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<(Warehouse Warehouse, StockBalance Balance)> AddWarehouseBalanceAsync(ApplicationDbContext context, decimal qtyAvailable)
    {
        var warehouse = new Warehouse { Code = "WH-001", Name = "Main warehouse" };
        var zone = new Zone { Code = "Z-001", Name = "Main zone", Warehouse = warehouse };
        var location = new Location { Code = "LOC-001", Name = "Main location", Zone = zone };
        var balance = new StockBalance
        {
            Product = new Product { Code = "P-001", Name = "Product 001" },
            Lot = new Lot { LotNo = "LOT-001" },
            Location = location,
            QtyAvailable = qtyAvailable
        };
        context.StockBalances.Add(balance);
        await context.SaveChangesAsync();

        return (warehouse, balance);
    }
}
