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
        await AddAdditionalBalanceAsync(context, warehouse, "QC", 9, QcService.QuarantineLocationCode);
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
    public async Task RecordCountResultsAsync_RejectsResultsForItemsOutsideTheOrder()
    {
        await using var context = CreateContext();
        var (warehouse, _) = await AddWarehouseBalanceAsync(context, qtyAvailable: 10);
        var cycleCountService = new CycleCountService(context, new InventoryService(context));
        var order = await cycleCountService.CreateCycleCountOrderAsync(warehouse.Id, "counter-1");

        var recorded = await cycleCountService.RecordCountResultsAsync(order.Id,
        [
            new CountResultDto { CycleCountItemId = order.Items.Single().Id + 100, CountedQty = 7 }
        ]);

        Assert.False(recorded);
        Assert.Equal("Draft", (await context.CycleCountOrders.SingleAsync()).Status);
    }

    [Fact]
    public async Task ApproveAndAdjustStockAsync_ReturnsFalseForPartialCountResults()
    {
        await using var context = CreateContext();
        var (warehouse, firstBalance) = await AddWarehouseBalanceAsync(context, qtyAvailable: 10);
        var secondBalance = await AddAdditionalBalanceAsync(context, warehouse, "SECOND", 8);
        var cycleCountService = new CycleCountService(context, new InventoryService(context));
        var order = await cycleCountService.CreateCycleCountOrderAsync(warehouse.Id, "counter-1");
        var firstItem = order.Items.Single(item => item.ProductId == firstBalance.ProductId);

        await cycleCountService.RecordCountResultsAsync(order.Id,
        [
            new CountResultDto { CycleCountItemId = firstItem.Id, CountedQty = 7 }
        ]);

        var approved = await cycleCountService.ApproveAndAdjustStockAsync(order.Id, "approver-1");

        Assert.False(approved);
        Assert.Equal("InProgress", (await context.CycleCountOrders.SingleAsync()).Status);
        Assert.Equal(10, await context.StockBalances.Where(stock => stock.Id == firstBalance.Id).Select(stock => stock.QtyAvailable).SingleAsync());
        Assert.Equal(8, await context.StockBalances.Where(stock => stock.Id == secondBalance.Id).Select(stock => stock.QtyAvailable).SingleAsync());
        Assert.Empty(await context.StockTransactions.ToListAsync());
    }

    [Fact]
    public async Task RecordCountResultsAsync_CompletesAllItemsBeforeApprovalAdjustsStock()
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
        Assert.Equal("Completed", (await context.CycleCountOrders.SingleAsync()).Status);

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

    private static async Task<StockBalance> AddAdditionalBalanceAsync(
        ApplicationDbContext context,
        Warehouse warehouse,
        string suffix,
        decimal qtyAvailable,
        string? locationCode = null)
    {
        var balance = new StockBalance
        {
            Product = new Product { Code = $"P-{suffix}", Name = $"Product {suffix}" },
            Lot = new Lot { LotNo = $"LOT-{suffix}" },
            Location = new Location
            {
                Code = locationCode ?? $"LOC-{suffix}",
                Name = $"Location {suffix}",
                Zone = new Zone { Code = $"Z-{suffix}", Name = $"Zone {suffix}", WarehouseId = warehouse.Id }
            },
            QtyAvailable = qtyAvailable
        };
        context.StockBalances.Add(balance);
        await context.SaveChangesAsync();
        return balance;
    }
}
