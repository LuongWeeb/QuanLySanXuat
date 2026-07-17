using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;
using WmsMes.Web.Hubs;

namespace WmsMes.Tests;

public class InventoryServiceTests
{
    [Fact]
    public async Task CompleteGoodsReceiptAsync_WhenPostCommitHubFails_ReturnsSuccessWithDurableInventory()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.GoodsReceipts.Add(new GoodsReceipt { Id = 1, ReceiptNo = "GR-HUB", Status = DocumentStatus.Draft,
            Lines = { new GoodsReceiptLine { ProductId = 1, LocationId = 1, LotNo = "LOT-HUB", Qty = 4 } } });
        await context.SaveChangesAsync();
        var (hub, logger, client) = ThrowingHub();

        var completed = await new InventoryService(context, hub.Object, logger.Object)
            .CompleteGoodsReceiptAsync(1, "user-1");

        Assert.True(completed);
        context.ChangeTracker.Clear();
        Assert.Equal(DocumentStatus.Completed, (await context.GoodsReceipts.SingleAsync()).Status);
        Assert.Equal(4, (await context.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Single(await context.StockTransactions.ToListAsync());
        client.Verify(x => x.SendCoreAsync("ReceiveStockUpdate", It.IsAny<object?[]>(), default), Times.Once);
        VerifyWarning(logger);
    }

    [Fact]
    public async Task CompleteGoodsIssueAsync_WhenPostCommitHubFails_ReturnsSuccessWithDurableInventory()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Customers.Add(new Customer { Id = 1, Code = "C", Name = "Customer" });
        context.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT", Qty = 5 });
        context.StockBalances.Add(new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 5 });
        context.GoodsIssues.Add(new GoodsIssue { Id = 1, IssueNo = "GI-HUB", CustomerId = 1, Status = DocumentStatus.Draft,
            Lines = { new GoodsIssueLine { ProductId = 1, LotId = 1, LocationId = 1, Qty = 2 } } });
        await context.SaveChangesAsync();
        var (hub, logger, client) = ThrowingHub();

        var completed = await new InventoryService(context, hub.Object, logger.Object)
            .CompleteGoodsIssueAsync(1, "user-1");

        Assert.True(completed);
        context.ChangeTracker.Clear();
        Assert.Equal(DocumentStatus.Completed, (await context.GoodsIssues.SingleAsync()).Status);
        Assert.Equal(3, (await context.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Single(await context.StockTransactions.ToListAsync());
        client.Verify(x => x.SendCoreAsync("ReceiveStockUpdate", It.IsAny<object?[]>(), default), Times.Once);
        VerifyWarning(logger);
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_WithAmbientTransaction_DefersSingleNotificationToOwner()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.GoodsReceipts.Add(new GoodsReceipt { Id = 1, ReceiptNo = "GR-AMBIENT", Status = DocumentStatus.Draft,
            Lines = { new GoodsReceiptLine { ProductId = 1, LocationId = 1, LotNo = "LOT-AMBIENT", Qty = 1 } } });
        await context.SaveChangesAsync();
        var client = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>(); clients.SetupGet(x => x.All).Returns(client.Object);
        var hub = new Mock<IHubContext<InventoryHub>>(); hub.SetupGet(x => x.Clients).Returns(clients.Object);
        var service = new InventoryService(context, hub.Object);
        await using var transaction = await context.Database.BeginTransactionAsync();

        Assert.True(await service.CompleteGoodsReceiptAsync(1, "user"));
        client.Verify(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), default), Times.Never);
        await transaction.CommitAsync();
        await service.NotifyStockChangedAsync();

        client.Verify(x => x.SendCoreAsync("ReceiveStockUpdate", It.IsAny<object?[]>(), default), Times.Once);
    }

    private static (Mock<IHubContext<InventoryHub>> hub, Mock<ILogger<InventoryService>> logger, Mock<IClientProxy> client) ThrowingHub()
    {
        var client = new Mock<IClientProxy>();
        client.Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub unavailable"));
        var clients = new Mock<IHubClients>(); clients.SetupGet(x => x.All).Returns(client.Object);
        var hub = new Mock<IHubContext<InventoryHub>>(); hub.SetupGet(x => x.Clients).Returns(clients.Object);
        return (hub, new Mock<ILogger<InventoryService>>(), client);
    }

    private static void VerifyWarning(Mock<ILogger<InventoryService>> logger) =>
        logger.Verify(x => x.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);

    [Fact]
    public async Task GetSuggestedLotsAsync_UsesFefoAndSplitsRequestedQuantity()
    {
        await using var context = CreateContext();
        await SeedRequiredMasterDataAsync(context);
        context.Products.Add(new Product
        {
            Id = 2,
            Code = "MILK",
            Name = "Milk",
            BaseUomId = 1,
            ShelfLifeDays = 30,
            IsLotTracked = true
        });
        context.Lots.AddRange(
            new Lot { Id = 1, ProductId = 2, LotNo = "LATE", ExpiryDate = DateTime.UtcNow.AddDays(20), Qty = 10 },
            new Lot { Id = 2, ProductId = 2, LotNo = "EARLY", ExpiryDate = DateTime.UtcNow.AddDays(5), Qty = 10 });
        context.StockBalances.AddRange(
            new StockBalance { ProductId = 2, LotId = 1, LocationId = 1, QtyAvailable = 10 },
            new StockBalance { ProductId = 2, LotId = 2, LocationId = 1, QtyAvailable = 10 });
        await context.SaveChangesAsync();

        var service = new InventoryService(context);

        var suggestions = (await service.GetSuggestedLotsAsync(2, 15)).ToList();

        Assert.Equal(2, suggestions.Count);
        Assert.Equal(2, suggestions[0].LotId);
        Assert.Equal(10, suggestions[0].QtyAvailable);
        Assert.Equal(1, suggestions[1].LotId);
        Assert.Equal(5, suggestions[1].QtyAvailable);
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_CreatesLotBalanceAndReceiptTransaction()
    {
        await using var context = CreateContext();
        await SeedRequiredMasterDataAsync(context);
        context.GoodsReceipts.Add(new GoodsReceipt
        {
            Id = 1,
            ReceiptNo = "GR-001",
            Status = DocumentStatus.Draft,
            Lines =
            {
                new GoodsReceiptLine
                {
                    ProductId = 1,
                    LocationId = 1,
                    LotNo = "LOT-001",
                    Qty = 12,
                    ExpiryDate = DateTime.UtcNow.AddDays(90)
                }
            }
        });
        await context.SaveChangesAsync();

        var service = new InventoryService(context);

        var completed = await service.CompleteGoodsReceiptAsync(1, "user-1");

        Assert.True(completed);
        var lot = await context.Lots.SingleAsync(l => l.LotNo == "LOT-001");
        Assert.Equal(12, lot.Qty);
        var balance = await context.StockBalances.SingleAsync(sb => sb.LotId == lot.Id);
        Assert.Equal(12, balance.QtyAvailable);
        var transaction = await context.StockTransactions.SingleAsync();
        Assert.Equal(TransactionType.Receipt, transaction.Type);
        Assert.Equal(12, transaction.Qty);
        Assert.Equal("GR-001", transaction.ReferenceNo);
        Assert.Equal(DocumentStatus.Completed, (await context.GoodsReceipts.FindAsync(1))!.Status);
    }

    [Fact]
    public async Task CompleteGoodsIssueAsync_ThrowsInvalidOperationException_WhenStockIsInsufficient()
    {
        await using var context = CreateContext();
        await SeedRequiredMasterDataAsync(context);
        context.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-001", Qty = 10 });
        context.StockBalances.Add(new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 10 });
        context.GoodsIssues.Add(new GoodsIssue
        {
            Id = 1,
            IssueNo = "GI-001",
            Status = DocumentStatus.Draft,
            Lines =
            {
                new GoodsIssueLine { ProductId = 1, LotId = 1, LocationId = 1, Qty = 15 }
            }
        });
        await context.SaveChangesAsync();

        var service = new InventoryService(context);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CompleteGoodsIssueAsync(1, "user-1"));

        Assert.Equal("Not enough available stock. Negative stock is not allowed.", exception.Message);
        Assert.Equal(10, (await context.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Empty(context.StockTransactions);
    }

    [Fact]
    public async Task StartStocktakeAsync_FreezesLocationBalancesAndCreatesCountingLines()
    {
        await using var context = CreateContext();
        await SeedRequiredMasterDataAsync(context);
        context.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-001", Qty = 10 });
        context.StockBalances.Add(new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 10, QtyOnHold = 2 });
        context.Stocktakes.Add(new Stocktake { Id = 1, StocktakeNo = "ST-001", LocationId = 1, Status = StocktakeStatus.Draft });
        await context.SaveChangesAsync();

        var service = new InventoryService(context);

        var started = await service.StartStocktakeAsync(1);

        Assert.True(started);
        var balance = await context.StockBalances.SingleAsync();
        Assert.Equal(0, balance.QtyAvailable);
        Assert.Equal(12, balance.QtyOnHold);
        var line = await context.StocktakeLines.SingleAsync();
        Assert.Equal(10, line.QtySystem);
        Assert.Equal(0, line.QtyCounted);
        Assert.Equal(StocktakeStatus.Counting, (await context.Stocktakes.FindAsync(1))!.Status);
    }

    [Fact]
    public async Task ApproveStocktakeAsync_ReleasesHoldAndWritesAdjustTransactionForDiscrepancy()
    {
        await using var context = CreateContext();
        await SeedRequiredMasterDataAsync(context);
        context.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-001", Qty = 10 });
        context.StockBalances.Add(new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 0, QtyOnHold = 10 });
        context.Stocktakes.Add(new Stocktake
        {
            Id = 1,
            StocktakeNo = "ST-001",
            LocationId = 1,
            Status = StocktakeStatus.AwaitingApproval,
            Lines =
            {
                new StocktakeLine { ProductId = 1, LotId = 1, QtySystem = 10, QtyCounted = 8 }
            }
        });
        await context.SaveChangesAsync();

        var service = new InventoryService(context);

        var approved = await service.ApproveStocktakeAsync(1, "manager-1");

        Assert.True(approved);
        var balance = await context.StockBalances.SingleAsync();
        Assert.Equal(8, balance.QtyAvailable);
        Assert.Equal(0, balance.QtyOnHold);
        var line = await context.StocktakeLines.SingleAsync();
        Assert.Equal(-2, line.QtyDiscrepancy);
        var transaction = await context.StockTransactions.SingleAsync();
        Assert.Equal(TransactionType.Adjust, transaction.Type);
        Assert.Equal(-2, transaction.Qty);
        Assert.Equal("ST-001", transaction.ReferenceNo);
        Assert.Equal(StocktakeStatus.Completed, (await context.Stocktakes.FindAsync(1))!.Status);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task SeedRequiredMasterDataAsync(ApplicationDbContext context)
    {
        context.UnitOfMeasures.Add(new UnitOfMeasure { Id = 1, Code = "PCS", Name = "Pieces" });
        context.Products.Add(new Product { Id = 1, Code = "P01", Name = "Product 01", BaseUomId = 1 });
        context.Warehouses.Add(new Warehouse { Id = 1, Code = "WH01", Name = "Main Warehouse" });
        context.Zones.Add(new Zone { Id = 1, Code = "Z01", Name = "Zone 01", WarehouseId = 1 });
        context.Locations.Add(new Location { Id = 1, Code = "LOC01", Name = "Location 01", ZoneId = 1 });
        await context.SaveChangesAsync();
    }
}
