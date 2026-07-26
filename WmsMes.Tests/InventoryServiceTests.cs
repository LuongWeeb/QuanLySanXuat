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
    public async Task AdjustStockAsync_WithStaleRelationalContexts_AllowsOnlyOneConditionalAdjustment()
    {
        var database = $"file:adjust-{Guid.NewGuid():N}?mode=memory&cache=shared";
        await using var keepAlive = new SqliteConnection($"Data Source={database}");
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={database}").Options;
        await using (var seed = new ApplicationDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            await SeedRequiredMasterDataAsync(seed);
            seed.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-ADJUST", Qty = 10 });
            seed.StockBalances.Add(new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 10 });
            await seed.SaveChangesAsync();
        }

        await using var firstContext = new ApplicationDbContext(options);
        await using var staleContext = new ApplicationDbContext(options);
        await firstContext.StockBalances.SingleAsync();
        await staleContext.StockBalances.SingleAsync();

        var first = await new InventoryService(firstContext).AdjustStockAsync(1, 1, 1, -7, "user-1", "CC-1");
        var stale = await new InventoryService(staleContext).AdjustStockAsync(1, 1, 1, -7, "user-2", "CC-2");

        Assert.True(first);
        Assert.False(stale);
        await using var verify = new ApplicationDbContext(options);
        Assert.Equal(3, (await verify.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Single(await verify.StockTransactions.ToListAsync());
    }

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
    public async Task CompleteGoodsReceiptAsync_WritesRunningBalanceAndLotValuationToLedger()
    {
        await using var context = CreateContext();
        await SeedRequiredMasterDataAsync(context);
        context.GoodsReceipts.Add(new GoodsReceipt
        {
            Id = 1,
            ReceiptNo = "GR-LEDGER",
            Status = DocumentStatus.Draft,
            Lines =
            {
                new GoodsReceiptLine
                {
                    ProductId = 1,
                    LocationId = 1,
                    LotNo = "LOT-LEDGER",
                    Qty = 12,
                    UnitPrice = 4.25m
                }
            }
        });
        await context.SaveChangesAsync();

        Assert.True(await new InventoryService(context).CompleteGoodsReceiptAsync(1, "user-1"));

        var transaction = await context.StockTransactions.SingleAsync();
        Assert.Equal(12m, transaction.QtyAfter);
        Assert.Equal(4.25m, transaction.ValuationRate);
        Assert.False(transaction.IsCancelled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CompleteGoodsReceiptAsync_RejectsNonPositiveQuantityBeforeWritingStockOrLedger(decimal qty)
    {
        await using var context = CreateContext();
        await SeedRequiredMasterDataAsync(context);
        context.GoodsReceipts.Add(new GoodsReceipt
        {
            Id = 1,
            ReceiptNo = "GR-INVALID-QTY",
            Status = DocumentStatus.Draft,
            Lines =
            {
                new GoodsReceiptLine
                {
                    ProductId = 1,
                    LocationId = 1,
                    LotNo = "LOT-INVALID-QTY",
                    Qty = qty
                }
            }
        });
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new InventoryService(context).CompleteGoodsReceiptAsync(1, "user-1"));

        Assert.Equal("Quantity must be greater than zero.", exception.Message);
        Assert.Empty(await context.Lots.ToListAsync());
        Assert.Empty(await context.StockBalances.ToListAsync());
        Assert.Empty(await context.StockTransactions.ToListAsync());
        Assert.Equal(DocumentStatus.Draft, (await context.GoodsReceipts.FindAsync(1))!.Status);
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_WhenLaterLineHasNonPositiveQuantity_RollsBackEarlierPosting()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.GoodsReceipts.Add(new GoodsReceipt
        {
            Id = 1,
            ReceiptNo = "GR-ROLLBACK",
            Status = DocumentStatus.Draft,
            Lines =
            {
                new GoodsReceiptLine { ProductId = 1, LocationId = 1, LotNo = "A-VALID", Qty = 5 },
                new GoodsReceiptLine { ProductId = 1, LocationId = 1, LotNo = "Z-INVALID", Qty = 0 }
            }
        });
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new InventoryService(context).CompleteGoodsReceiptAsync(1, "user-1"));

        context.ChangeTracker.Clear();
        Assert.Empty(await context.Lots.ToListAsync());
        Assert.Empty(await context.StockBalances.ToListAsync());
        Assert.Empty(await context.StockTransactions.ToListAsync());
        Assert.Equal(DocumentStatus.Draft, (await context.GoodsReceipts.FindAsync(1))!.Status);
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
    public async Task CompleteGoodsIssueAsync_WritesRemainingBalanceAndLotValuationToLedger()
    {
        await using var context = CreateContext();
        await SeedRequiredMasterDataAsync(context);
        context.Customers.Add(new Customer { Id = 1, Code = "C1", Name = "Customer 1" });
        context.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-LEDGER", Qty = 10, UnitPrice = 7.5m });
        context.StockBalances.Add(new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 10 });
        context.GoodsIssues.Add(new GoodsIssue
        {
            Id = 1,
            IssueNo = "GI-LEDGER",
            CustomerId = 1,
            Status = DocumentStatus.Draft,
            Lines = { new GoodsIssueLine { ProductId = 1, LotId = 1, LocationId = 1, Qty = 3 } }
        });
        await context.SaveChangesAsync();

        Assert.True(await new InventoryService(context).CompleteGoodsIssueAsync(1, "user-1"));

        var transaction = await context.StockTransactions.SingleAsync();
        Assert.Equal(-3m, transaction.Qty);
        Assert.Equal(7m, transaction.QtyAfter);
        Assert.Equal(7.5m, transaction.ValuationRate);
        Assert.False(transaction.IsCancelled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task CompleteGoodsIssueAsync_RejectsNonPositiveQuantityBeforeChangingStockOrLedger(decimal qty)
    {
        await using var context = CreateContext();
        await SeedRequiredMasterDataAsync(context);
        context.Customers.Add(new Customer { Id = 1, Code = "C1", Name = "Customer 1" });
        context.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-INVALID-QTY", Qty = 10, UnitPrice = 7.5m });
        context.StockBalances.Add(new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 10 });
        context.GoodsIssues.Add(new GoodsIssue
        {
            Id = 1,
            IssueNo = "GI-INVALID-QTY",
            CustomerId = 1,
            Status = DocumentStatus.Draft,
            Lines = { new GoodsIssueLine { ProductId = 1, LotId = 1, LocationId = 1, Qty = qty } }
        });
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new InventoryService(context).CompleteGoodsIssueAsync(1, "user-1"));

        Assert.Equal("Quantity must be greater than zero.", exception.Message);
        Assert.Equal(10m, (await context.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Empty(await context.StockTransactions.ToListAsync());
        Assert.Equal(DocumentStatus.Draft, (await context.GoodsIssues.FindAsync(1))!.Status);
    }

    [Fact]
    public async Task CompleteGoodsIssueAsync_WhenLaterLineHasNonPositiveQuantity_RollsBackEarlierPosting()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Customers.Add(new Customer { Id = 1, Code = "C1", Name = "Customer 1" });
        context.Lots.AddRange(
            new Lot { Id = 1, ProductId = 1, LotNo = "LOT-1", Qty = 10 },
            new Lot { Id = 2, ProductId = 1, LotNo = "LOT-2", Qty = 10 });
        context.StockBalances.AddRange(
            new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 10 },
            new StockBalance { ProductId = 1, LotId = 2, LocationId = 1, QtyAvailable = 10 });
        context.GoodsIssues.Add(new GoodsIssue
        {
            Id = 1,
            IssueNo = "GI-ROLLBACK",
            CustomerId = 1,
            Status = DocumentStatus.Draft,
            Lines =
            {
                new GoodsIssueLine { ProductId = 1, LotId = 1, LocationId = 1, Qty = 3 },
                new GoodsIssueLine { ProductId = 1, LotId = 2, LocationId = 1, Qty = 0 }
            }
        });
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new InventoryService(context).CompleteGoodsIssueAsync(1, "user-1"));

        context.ChangeTracker.Clear();
        Assert.Equal(new[] { 10m, 10m }, await context.StockBalances.OrderBy(balance => balance.LotId)
            .Select(balance => balance.QtyAvailable).ToListAsync());
        Assert.Empty(await context.StockTransactions.ToListAsync());
        Assert.Equal(DocumentStatus.Draft, (await context.GoodsIssues.FindAsync(1))!.Status);
    }

    [Fact]
    public async Task CompleteGoodsIssueAsync_OnRelationalStore_WritesPersistedLedgerBalanceAndLotValuation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Customers.Add(new Customer { Id = 1, Code = "C1", Name = "Customer 1" });
        context.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-SQLITE", Qty = 10, UnitPrice = 7.5m });
        context.StockBalances.Add(new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 10 });
        context.GoodsIssues.Add(new GoodsIssue
        {
            Id = 1,
            IssueNo = "GI-SQLITE",
            CustomerId = 1,
            Status = DocumentStatus.Draft,
            Lines = { new GoodsIssueLine { ProductId = 1, LotId = 1, LocationId = 1, Qty = 3 } }
        });
        await context.SaveChangesAsync();

        Assert.True(await new InventoryService(context).CompleteGoodsIssueAsync(1, "user-1"));

        context.ChangeTracker.Clear();
        Assert.Equal(7m, (await context.StockBalances.SingleAsync()).QtyAvailable);
        var transaction = await context.StockTransactions.SingleAsync();
        Assert.Equal(-3m, transaction.Qty);
        Assert.Equal(7m, transaction.QtyAfter);
        Assert.Equal(7.5m, transaction.ValuationRate);
        Assert.False(transaction.IsCancelled);
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_ProcessesLinesInDeterministicTupleOrder()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Products.Add(new Product
        {
            Id = 2,
            Code = "P02",
            Name = "Product 02",
            BaseUomId = 1
        });
        context.Locations.Add(new Location
        {
            Id = 2,
            Code = "LOC02",
            Name = "Location 02",
            ZoneId = 1
        });
        context.GoodsReceipts.Add(new GoodsReceipt
        {
            Id = 1,
            ReceiptNo = "GR-ORDER",
            Status = DocumentStatus.Draft,
            Lines =
            {
                new GoodsReceiptLine
                {
                    ProductId = 2,
                    LotNo = "LOT-Z",
                    LocationId = 2,
                    Qty = 1
                },
                new GoodsReceiptLine
                {
                    ProductId = 1,
                    LotNo = "LOT-B",
                    LocationId = 1,
                    Qty = 1
                },
                new GoodsReceiptLine
                {
                    ProductId = 1,
                    LotNo = "LOT-A",
                    LocationId = 2,
                    Qty = 1
                }
            }
        });
        await context.SaveChangesAsync();

        Assert.True(await new InventoryService(context)
            .CompleteGoodsReceiptAsync(1, "warehouse"));

        context.ChangeTracker.Clear();
        var processed = await context.StockTransactions
            .Include(transaction => transaction.Lot)
            .OrderBy(transaction => transaction.Id)
            .Select(transaction => new
            {
                transaction.ProductId,
                transaction.Lot!.LotNo,
                transaction.LocationId
            })
            .ToListAsync();
        Assert.Equal(
            new[] { "1:LOT-A:2", "1:LOT-B:1", "2:LOT-Z:2" },
            processed.Select(item => $"{item.ProductId}:{item.LotNo}:{item.LocationId}"));
    }

    [Fact]
    public async Task CompleteGoodsIssueAsync_ProcessesLinesInDeterministicTupleOrder()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Customers.Add(new Customer { Id = 1, Code = "C", Name = "Customer" });
        context.Products.Add(new Product
        {
            Id = 2,
            Code = "P02",
            Name = "Product 02",
            BaseUomId = 1
        });
        context.Locations.Add(new Location
        {
            Id = 2,
            Code = "LOC02",
            Name = "Location 02",
            ZoneId = 1
        });
        context.Lots.AddRange(
            new Lot { Id = 1, ProductId = 1, LotNo = "LOT-1", Qty = 5 },
            new Lot { Id = 2, ProductId = 1, LotNo = "LOT-2", Qty = 5 },
            new Lot { Id = 3, ProductId = 2, LotNo = "LOT-3", Qty = 5 });
        context.StockBalances.AddRange(
            new StockBalance { ProductId = 1, LotId = 1, LocationId = 2, QtyAvailable = 5 },
            new StockBalance { ProductId = 1, LotId = 2, LocationId = 1, QtyAvailable = 5 },
            new StockBalance { ProductId = 2, LotId = 3, LocationId = 2, QtyAvailable = 5 });
        context.GoodsIssues.Add(new GoodsIssue
        {
            Id = 1,
            IssueNo = "GI-ORDER",
            CustomerId = 1,
            Status = DocumentStatus.Draft,
            Lines =
            {
                new GoodsIssueLine { ProductId = 2, LotId = 3, LocationId = 2, Qty = 1 },
                new GoodsIssueLine { ProductId = 1, LotId = 2, LocationId = 1, Qty = 1 },
                new GoodsIssueLine { ProductId = 1, LotId = 1, LocationId = 2, Qty = 1 }
            }
        });
        await context.SaveChangesAsync();

        Assert.True(await new InventoryService(context)
            .CompleteGoodsIssueAsync(1, "warehouse"));

        context.ChangeTracker.Clear();
        var processed = await context.StockTransactions
            .OrderBy(transaction => transaction.Id)
            .Select(transaction =>
                $"{transaction.ProductId}:{transaction.LotId}:{transaction.LocationId}")
            .ToListAsync();
        Assert.Equal(
            new[] { "1:1:2", "1:2:1", "2:3:2" },
            processed);
    }

    [Fact]
    public async Task CompleteGoodsIssueAsync_RejectsQuarantineBalanceAtomically()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Customers.Add(new Customer { Id = 1, Code = "C", Name = "Customer" });
        var quarantine = await context.Locations.FindAsync(1);
        quarantine!.Code = QcService.QuarantineLocationCode;
        context.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-Q", Qty = 5 });
        context.StockBalances.Add(new StockBalance
        {
            ProductId = 1,
            LotId = 1,
            LocationId = 1,
            QtyAvailable = 5
        });
        context.GoodsIssues.Add(new GoodsIssue
        {
            Id = 1,
            IssueNo = "GI-Q",
            CustomerId = 1,
            Status = DocumentStatus.Draft,
            Lines =
            {
                new GoodsIssueLine
                {
                    ProductId = 1,
                    LotId = 1,
                    LocationId = 1,
                    Qty = 2
                }
            }
        });
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InventoryService(context).CompleteGoodsIssueAsync(1, "warehouse"));

        context.ChangeTracker.Clear();
        Assert.Equal(5, (await context.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Equal(DocumentStatus.Draft, (await context.GoodsIssues.SingleAsync()).Status);
        Assert.Empty(await context.StockTransactions.ToListAsync());
    }

    [Fact]
    public async Task CancelGoodsReceiptAsync_WhenReceiptIsNotCompleted_ReturnsFalseWithoutChanges()
    {
        await using var context = CreateContext();
        await SeedRequiredMasterDataAsync(context);
        context.GoodsReceipts.Add(new GoodsReceipt
        {
            Id = 1,
            ReceiptNo = "GR-DRAFT",
            Status = DocumentStatus.Draft
        });
        await context.SaveChangesAsync();

        var cancelled = await new InventoryService(context)
            .CancelGoodsReceiptAsync(1, "warehouse");

        Assert.False(cancelled);
        Assert.Equal(DocumentStatus.Draft, (await context.GoodsReceipts.SingleAsync()).Status);
        Assert.Empty(await context.StockTransactions.ToListAsync());
    }

    [Fact]
    public async Task CancelGoodsIssueAsync_WhenIssueIsNotCompleted_ReturnsFalseWithoutChanges()
    {
        await using var context = CreateContext();
        await SeedRequiredMasterDataAsync(context);
        context.Customers.Add(new Customer { Id = 1, Code = "C", Name = "Customer" });
        context.GoodsIssues.Add(new GoodsIssue
        {
            Id = 1,
            IssueNo = "GI-DRAFT",
            CustomerId = 1,
            Status = DocumentStatus.Draft
        });
        await context.SaveChangesAsync();

        var cancelled = await new InventoryService(context)
            .CancelGoodsIssueAsync(1, "warehouse");

        Assert.False(cancelled);
        Assert.Equal(DocumentStatus.Draft, (await context.GoodsIssues.SingleAsync()).Status);
        Assert.Empty(await context.StockTransactions.ToListAsync());
    }

    [Fact]
    public async Task CancelGoodsReceiptAsync_UsesExactReceiptLotAndWritesReversalLedger()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Lots.AddRange(
            new Lot { Id = 1, ProductId = 1, LotNo = "LOT-TARGET", Qty = 8, UnitPrice = 6.25m },
            new Lot { Id = 2, ProductId = 1, LotNo = "LOT-OTHER", Qty = 100, UnitPrice = 9m });
        context.StockBalances.AddRange(
            new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 8 },
            new StockBalance { ProductId = 1, LotId = 2, LocationId = 1, QtyAvailable = 100 });
        context.GoodsReceipts.Add(new GoodsReceipt
        {
            Id = 1,
            ReceiptNo = "GR-CANCEL",
            Status = DocumentStatus.Completed,
            Lines =
            {
                new GoodsReceiptLine
                {
                    ProductId = 1,
                    LocationId = 1,
                    LotNo = "LOT-TARGET",
                    Qty = 5,
                    UnitPrice = 4m
                }
            }
        });
        await context.SaveChangesAsync();

        var cancelled = await new InventoryService(context)
            .CancelGoodsReceiptAsync(1, "warehouse");

        Assert.True(cancelled);
        Assert.Equal(3m, (await context.StockBalances.FindAsync(1))!.QtyAvailable);
        Assert.Equal(3m, (await context.Lots.FindAsync(1))!.Qty);
        context.ChangeTracker.Clear();
        Assert.Equal(
            new[] { 3m, 100m },
            await context.StockBalances.OrderBy(balance => balance.LotId)
                .Select(balance => balance.QtyAvailable)
                .ToArrayAsync());
        Assert.Equal(
            new[] { 3m, 100m },
            await context.Lots.OrderBy(lot => lot.Id).Select(lot => lot.Qty).ToArrayAsync());
        var transaction = await context.StockTransactions.SingleAsync();
        Assert.Equal(TransactionType.Receipt, transaction.Type);
        Assert.Equal(1, transaction.LotId);
        Assert.Equal(-5m, transaction.Qty);
        Assert.Equal(3m, transaction.QtyAfter);
        Assert.Equal(6.25m, transaction.ValuationRate);
        Assert.True(transaction.IsCancelled);
        Assert.Equal("GR-CANCEL", transaction.ReferenceNo);
        Assert.Equal("warehouse", transaction.UserId);
        Assert.Equal(DocumentStatus.Cancelled, (await context.GoodsReceipts.SingleAsync()).Status);
    }

    [Fact]
    public async Task CancelGoodsReceiptAsync_WhenLaterExactLotIsInsufficient_RollsBackAllChanges()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Lots.AddRange(
            new Lot { Id = 1, ProductId = 1, LotNo = "LOT-A", Qty = 10, UnitPrice = 2m },
            new Lot { Id = 2, ProductId = 1, LotNo = "LOT-B", Qty = 2, UnitPrice = 3m },
            new Lot { Id = 3, ProductId = 1, LotNo = "LOT-C", Qty = 100, UnitPrice = 4m });
        context.StockBalances.AddRange(
            new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 10 },
            new StockBalance { ProductId = 1, LotId = 2, LocationId = 1, QtyAvailable = 2 },
            new StockBalance { ProductId = 1, LotId = 3, LocationId = 1, QtyAvailable = 100 });
        context.GoodsReceipts.Add(new GoodsReceipt
        {
            Id = 1,
            ReceiptNo = "GR-ROLLBACK-CANCEL",
            Status = DocumentStatus.Completed,
            Lines =
            {
                new GoodsReceiptLine { ProductId = 1, LocationId = 1, LotNo = "LOT-A", Qty = 4 },
                new GoodsReceiptLine { ProductId = 1, LocationId = 1, LotNo = "LOT-B", Qty = 5 }
            }
        });
        await context.SaveChangesAsync();
        var product = await context.Products.FindAsync(1);
        product!.Name = "Locally edited product";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InventoryService(context).CancelGoodsReceiptAsync(1, "warehouse"));

        Assert.Contains("Cần 5, Hiện có 2", exception.Message);
        Assert.True(context.Entry(product).Property(candidate => candidate.Name).IsModified);
        Assert.False(context.Entry(product).Property(candidate => candidate.Code).IsModified);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(
            new[] { 10m, 2m, 100m },
            await context.StockBalances.OrderBy(balance => balance.LotId)
                .Select(balance => balance.QtyAvailable)
                .ToArrayAsync());
        Assert.Equal(
            new[] { 10m, 2m, 100m },
            await context.Lots.OrderBy(lot => lot.Id).Select(lot => lot.Qty).ToArrayAsync());
        Assert.Empty(await context.StockTransactions.ToListAsync());
        Assert.Equal(DocumentStatus.Completed, (await context.GoodsReceipts.SingleAsync()).Status);
    }

    [Fact]
    public async Task CancelGoodsIssueAsync_RestoresStockAndWritesReversalLedger()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Customers.Add(new Customer { Id = 1, Code = "C", Name = "Customer" });
        context.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-ISSUE", Qty = 10, UnitPrice = 7.5m });
        context.StockBalances.Add(new StockBalance
        {
            ProductId = 1,
            LotId = 1,
            LocationId = 1,
            QtyAvailable = 3
        });
        context.GoodsIssues.Add(new GoodsIssue
        {
            Id = 1,
            IssueNo = "GI-CANCEL",
            CustomerId = 1,
            Status = DocumentStatus.Completed,
            Lines =
            {
                new GoodsIssueLine { ProductId = 1, LotId = 1, LocationId = 1, Qty = 2 }
            }
        });
        await context.SaveChangesAsync();

        var cancelled = await new InventoryService(context)
            .CancelGoodsIssueAsync(1, "warehouse");

        Assert.True(cancelled);
        Assert.Equal(5m, (await context.StockBalances.SingleAsync()).QtyAvailable);
        context.ChangeTracker.Clear();
        Assert.Equal(5m, (await context.StockBalances.SingleAsync()).QtyAvailable);
        var transaction = await context.StockTransactions.SingleAsync();
        Assert.Equal(TransactionType.Issue, transaction.Type);
        Assert.Equal(2m, transaction.Qty);
        Assert.Equal(5m, transaction.QtyAfter);
        Assert.Equal(7.5m, transaction.ValuationRate);
        Assert.True(transaction.IsCancelled);
        Assert.Equal("GI-CANCEL", transaction.ReferenceNo);
        Assert.Equal("warehouse", transaction.UserId);
        Assert.Equal(DocumentStatus.Cancelled, (await context.GoodsIssues.SingleAsync()).Status);
    }

    [Fact]
    public async Task CancelGoodsIssueAsync_WhenRepeatedKeyBalanceIsMissing_CreatesOneRestoredBalance()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Customers.Add(new Customer { Id = 1, Code = "C", Name = "Customer" });
        context.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-ISSUE", Qty = 10, UnitPrice = 7.5m });
        context.GoodsIssues.Add(new GoodsIssue
        {
            Id = 1,
            IssueNo = "GI-MISSING-BALANCE",
            CustomerId = 1,
            Status = DocumentStatus.Completed,
            Lines =
            {
                new GoodsIssueLine { ProductId = 1, LotId = 1, LocationId = 1, Qty = 2 },
                new GoodsIssueLine { ProductId = 1, LotId = 1, LocationId = 1, Qty = 3 }
            }
        });
        await context.SaveChangesAsync();

        Assert.True(await new InventoryService(context)
            .CancelGoodsIssueAsync(1, "warehouse"));

        var balance = await context.StockBalances.SingleAsync();
        Assert.Equal(1, balance.ProductId);
        Assert.Equal(1, balance.LotId);
        Assert.Equal(1, balance.LocationId);
        Assert.Equal(5m, balance.QtyAvailable);
        Assert.Equal(
            new[] { 2m, 5m },
            await context.StockTransactions.OrderBy(transaction => transaction.Id)
                .Select(transaction => transaction.QtyAfter)
                .ToArrayAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CancelGoodsReceiptAsync_WhenAnyLineIsNonPositive_LeavesCompletedReceiptUnchanged(decimal qty)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Lots.AddRange(
            new Lot { Id = 1, ProductId = 1, LotNo = "LOT-A", Qty = 10 },
            new Lot { Id = 2, ProductId = 1, LotNo = "LOT-B", Qty = 10 });
        context.StockBalances.AddRange(
            new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 10 },
            new StockBalance { ProductId = 1, LotId = 2, LocationId = 1, QtyAvailable = 10 });
        context.GoodsReceipts.Add(new GoodsReceipt
        {
            Id = 1,
            ReceiptNo = "GR-INVALID-CANCEL",
            Status = DocumentStatus.Completed,
            Lines =
            {
                new GoodsReceiptLine { ProductId = 1, LocationId = 1, LotNo = "LOT-A", Qty = 2 },
                new GoodsReceiptLine { ProductId = 1, LocationId = 1, LotNo = "LOT-B", Qty = qty }
            }
        });
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InventoryService(context).CancelGoodsReceiptAsync(1, "warehouse"));

        Assert.Equal("Quantity must be greater than zero.", exception.Message);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(
            new[] { 10m, 10m },
            await context.StockBalances.OrderBy(balance => balance.LotId)
                .Select(balance => balance.QtyAvailable)
                .ToArrayAsync());
        Assert.Equal(
            new[] { 10m, 10m },
            await context.Lots.OrderBy(lot => lot.Id).Select(lot => lot.Qty).ToArrayAsync());
        Assert.Empty(await context.StockTransactions.ToListAsync());
        Assert.Equal(DocumentStatus.Completed, (await context.GoodsReceipts.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CancelGoodsIssueAsync_WhenAnyLineIsNonPositive_LeavesCompletedIssueUnchanged(decimal qty)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Customers.Add(new Customer { Id = 1, Code = "C", Name = "Customer" });
        context.Lots.AddRange(
            new Lot { Id = 1, ProductId = 1, LotNo = "LOT-A", Qty = 10 },
            new Lot { Id = 2, ProductId = 1, LotNo = "LOT-B", Qty = 10 });
        context.StockBalances.AddRange(
            new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 10 },
            new StockBalance { ProductId = 1, LotId = 2, LocationId = 1, QtyAvailable = 10 });
        context.GoodsIssues.Add(new GoodsIssue
        {
            Id = 1,
            IssueNo = "GI-INVALID-CANCEL",
            CustomerId = 1,
            Status = DocumentStatus.Completed,
            Lines =
            {
                new GoodsIssueLine { ProductId = 1, LotId = 1, LocationId = 1, Qty = 2 },
                new GoodsIssueLine { ProductId = 1, LotId = 2, LocationId = 1, Qty = qty }
            }
        });
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InventoryService(context).CancelGoodsIssueAsync(1, "warehouse"));

        Assert.Equal("Quantity must be greater than zero.", exception.Message);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(
            new[] { 10m, 10m },
            await context.StockBalances.OrderBy(balance => balance.LotId)
                .Select(balance => balance.QtyAvailable)
                .ToArrayAsync());
        Assert.Empty(await context.StockTransactions.ToListAsync());
        Assert.Equal(DocumentStatus.Completed, (await context.GoodsIssues.SingleAsync()).Status);
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
