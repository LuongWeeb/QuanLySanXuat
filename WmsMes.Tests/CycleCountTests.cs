using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.DTOs;
using WmsMes.Web.Services;
using System.Text.RegularExpressions;

namespace WmsMes.Tests;

public class CycleCountTests
{
    [Fact]
    public async Task CreateOrderAsync_UsesDailySequenceAndSnapshotsOnlyPositiveTotalStock()
    {
        await using var context = CreateContext();
        var (warehouse, availableBalance) =
            await AddWarehouseBalanceAsync(context, qtyAvailable: 100);
        var heldOnly = await AddAdditionalBalanceAsync(
            context, warehouse, "HELD", qtyAvailable: 0);
        heldOnly.QtyOnHold = 5;
        var empty = await AddAdditionalBalanceAsync(
            context, warehouse, "EMPTY", qtyAvailable: 0);
        await context.SaveChangesAsync();
        var service = new CycleCountService(context, new InventoryService(context));

        var first = await service.CreateOrderAsync(warehouse.Id, "counter-1");
        var second = await service.CreateOrderAsync(warehouse.Id, "counter-1");

        Assert.Matches(
            new Regex($@"^CC-{DateTime.UtcNow:yyyyMMdd}-\d{{3}}$"),
            first.CountNumber);
        Assert.EndsWith("-001", first.CountNumber);
        Assert.EndsWith("-002", second.CountNumber);
        Assert.Equal(2, first.Items.Count);
        Assert.Equal(
            100,
            first.Items.Single(item =>
                item.ProductId == availableBalance.ProductId).SystemQty);
        Assert.Equal(
            0,
            first.Items.Single(item =>
                item.ProductId == heldOnly.ProductId).SystemQty);
        Assert.DoesNotContain(
            first.Items,
            item => item.ProductId == empty.ProductId);
    }

    [Fact]
    public async Task UpdateCountedQtysAndApproveLedger_UsePlanContract()
    {
        await using var context = CreateContext();
        var (warehouse, balance) =
            await AddWarehouseBalanceAsync(context, qtyAvailable: 100);
        ICycleCountService service =
            new CycleCountService(context, new InventoryService(context));
        var order = await service.CreateOrderAsync(warehouse.Id, "counter-1");
        var item = Assert.Single(order.Items);

        Assert.True(await service.UpdateCountedQtysAsync(
            order.Id,
            new Dictionary<int, decimal> { [item.Id] = 90 }));
        Assert.True(await service.ApproveAndAdjustLedgerAsync(
            order.Id,
            "manager-1"));

        Assert.Equal(
            90,
            await context.StockBalances
                .Where(stock => stock.Id == balance.Id)
                .Select(stock => stock.QtyAvailable)
                .SingleAsync());
        var transaction = Assert.Single(await context.StockTransactions.ToListAsync());
        Assert.Equal(TransactionType.Adjust, transaction.Type);
        Assert.Equal(-10, transaction.Qty);
    }

    [Fact]
    public async Task RecordCountResultsAsync_RejectsNegativeBatchWithoutMutatingAnyItem()
    {
        await using var context = CreateContext();
        var (warehouse, firstBalance) = await AddWarehouseBalanceAsync(context, qtyAvailable: 10);
        await AddAdditionalBalanceAsync(context, warehouse, "SECOND", 8);
        var service = new CycleCountService(context, new InventoryService(context));
        var order = await service.CreateCycleCountOrderAsync(warehouse.Id, "counter-1");

        var recorded = await service.RecordCountResultsAsync(order.Id,
        [
            new CountResultDto { CycleCountItemId = order.Items.Single(item => item.ProductId == firstBalance.ProductId).Id, CountedQty = 7 },
            new CountResultDto { CycleCountItemId = order.Items.Single(item => item.ProductId != firstBalance.ProductId).Id, CountedQty = -1 }
        ]);

        Assert.False(recorded);
        Assert.All(await context.CycleCountItems.ToListAsync(), item => Assert.Null(item.CountedQty));
        Assert.Equal("Draft", (await context.CycleCountOrders.SingleAsync()).Status);
    }

    [Fact]
    public async Task RecordCountResultsAsync_RejectsDuplicateItemIdsWithoutMutation()
    {
        await using var context = CreateContext();
        var (warehouse, _) = await AddWarehouseBalanceAsync(context, qtyAvailable: 10);
        var service = new CycleCountService(context, new InventoryService(context));
        var order = await service.CreateCycleCountOrderAsync(warehouse.Id, "counter-1");
        var itemId = order.Items.Single().Id;

        var recorded = await service.RecordCountResultsAsync(order.Id,
        [
            new CountResultDto { CycleCountItemId = itemId, CountedQty = 7 },
            new CountResultDto { CycleCountItemId = itemId, CountedQty = 8 }
        ]);

        Assert.False(recorded);
        Assert.Null((await context.CycleCountItems.SingleAsync()).CountedQty);
        Assert.Equal("Draft", (await context.CycleCountOrders.SingleAsync()).Status);
    }

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

    [Fact]
    public async Task ApproveAndAdjustStockAsync_WithStaleRelationalContexts_AdjustsOnlyOnce()
    {
        var (keepAlive, options) = await CreateSharedSqliteContextOptionsAsync("approve");
        await using (keepAlive)
        {
            await SeedCompletedOrderAsync(options, countedQty: 7);
            await using var firstContext = new ApplicationDbContext(options);
            await using var staleContext = new ApplicationDbContext(options);
            await firstContext.CycleCountOrders.Include(order => order.Items).SingleAsync();
            await staleContext.CycleCountOrders.Include(order => order.Items).SingleAsync();
            await firstContext.StockBalances.SingleAsync();
            await staleContext.StockBalances.SingleAsync();

            var first = await new CycleCountService(firstContext, new InventoryService(firstContext))
                .ApproveAndAdjustStockAsync(1, "approver-1");
            var stale = await new CycleCountService(staleContext, new InventoryService(staleContext))
                .ApproveAndAdjustStockAsync(1, "approver-2");

            Assert.True(first);
            Assert.False(stale);
            await using var verify = new ApplicationDbContext(options);
            Assert.Equal("Approved", (await verify.CycleCountOrders.SingleAsync()).Status);
            Assert.Equal(7, (await verify.StockBalances.SingleAsync()).QtyAvailable);
            Assert.Single(await verify.StockTransactions.ToListAsync());
        }
    }

    [Fact]
    public async Task ApproveAndAdjustStockAsync_WhenAdjustmentFails_RollsBackRelationalStatusClaim()
    {
        var (keepAlive, options) = await CreateSharedSqliteContextOptionsAsync("rollback");
        await using (keepAlive)
        {
            await SeedCompletedOrderAsync(options, countedQty: 0);
            await using var context = new ApplicationDbContext(options);
            await context.StockBalances.ExecuteUpdateAsync(setters => setters.SetProperty(balance => balance.QtyAvailable, 5));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new CycleCountService(context, new InventoryService(context)).ApproveAndAdjustStockAsync(1, "approver-1"));

            context.ChangeTracker.Clear();
            Assert.Equal("Completed", (await context.CycleCountOrders.SingleAsync()).Status);
            Assert.Equal(5, (await context.StockBalances.SingleAsync()).QtyAvailable);
            Assert.Empty(await context.StockTransactions.ToListAsync());
        }
    }

    [Fact]
    public async Task ApproveAndAdjustStockAsync_WithAmbientTransaction_DoesNotNotify()
    {
        var (keepAlive, options) = await CreateSharedSqliteContextOptionsAsync("ambient");
        await using (keepAlive)
        {
            await SeedCompletedOrderAsync(options, countedQty: 10);
            await using var context = new ApplicationDbContext(options);
            var inventory = new Mock<IInventoryService>();
            inventory.Setup(service => service.AdjustStockAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            await using var transaction = await context.Database.BeginTransactionAsync();

            Assert.True(await new CycleCountService(context, inventory.Object).ApproveAndAdjustStockAsync(1, "approver-1"));
            inventory.Verify(service => service.NotifyStockChangedAsync(), Times.Never);
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task ApproveAndAdjustStockAsync_WhenPostCommitNotificationFails_ReturnsSuccess()
    {
        await using var context = CreateContext();
        var (warehouse, _) = await AddWarehouseBalanceAsync(context, qtyAvailable: 10);
        var inventory = new Mock<IInventoryService>();
        inventory.Setup(service => service.AdjustStockAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        inventory.Setup(service => service.NotifyStockChangedAsync()).ThrowsAsync(new InvalidOperationException("hub unavailable"));
        var service = new CycleCountService(context, inventory.Object);
        var order = await service.CreateCycleCountOrderAsync(warehouse.Id, "counter-1");
        await service.RecordCountResultsAsync(order.Id,
        [
            new CountResultDto { CycleCountItemId = order.Items.Single().Id, CountedQty = 7 }
        ]);

        Assert.True(await service.ApproveAndAdjustStockAsync(order.Id, "approver-1"));
        Assert.Equal("Approved", (await context.CycleCountOrders.SingleAsync()).Status);
        inventory.Verify(service => service.NotifyStockChangedAsync(), Times.Once);
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
        var product = new Product { Code = "P-001", Name = "Product 001", BaseUomId = 1 };
        var balance = new StockBalance
        {
            Product = product,
            Lot = new Lot { LotNo = "LOT-001", Product = product },
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

    private static async Task<(SqliteConnection KeepAlive, DbContextOptions<ApplicationDbContext> Options)> CreateSharedSqliteContextOptionsAsync(string prefix)
    {
        var database = $"file:cycle-{prefix}-{Guid.NewGuid():N}?mode=memory&cache=shared";
        var keepAlive = new SqliteConnection($"Data Source={database}");
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={database}").Options;
        return (keepAlive, options);
    }

    private static async Task SeedCompletedOrderAsync(DbContextOptions<ApplicationDbContext> options, decimal countedQty)
    {
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        context.UnitOfMeasures.Add(new UnitOfMeasure { Id = 1, Code = "PCS", Name = "Pieces" });
        await context.SaveChangesAsync();
        var (warehouse, balance) = await AddWarehouseBalanceAsync(context, qtyAvailable: 10);
        context.CycleCountOrders.Add(new CycleCountOrder
        {
            Id = 1,
            CountNumber = "CC-SQLITE",
            WarehouseId = warehouse.Id,
            CreatedBy = "counter-1",
            Status = "Completed",
            Items =
            {
                new CycleCountItem
                {
                    ProductId = balance.ProductId,
                    LotId = balance.LotId,
                    LocationId = balance.LocationId,
                    SystemQty = 10,
                    CountedQty = countedQty
                }
            }
        });
        await context.SaveChangesAsync();
    }
}
