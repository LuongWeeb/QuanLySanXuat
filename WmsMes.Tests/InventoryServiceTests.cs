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
            seed.Lots.Add(new Lot
            {
                Id = 1,
                ProductId = 1,
                LotNo = "LOT-ADJUST",
                Qty = 10,
                UnitPrice = 4.75m
            });
            seed.StockBalances.Add(new StockBalance
            {
                ProductId = 1,
                LotId = 1,
                LocationId = 1,
                QtyAvailable = 10,
                QtyReserved = 5,
                QtyOnHold = 7
            });
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
        var balance = await verify.StockBalances.SingleAsync();
        Assert.Equal(3, balance.QtyAvailable);
        Assert.Equal(5, balance.QtyReserved);
        Assert.Equal(7, balance.QtyOnHold);
        var transaction = Assert.Single(await verify.StockTransactions.ToListAsync());
        Assert.Equal(balance.QtyAvailable, transaction.QtyAfter);
        Assert.Equal(4.75m, transaction.ValuationRate);
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_WhenSameDraftIsCompletedConcurrently_OneSucceedsAndOneReturnsFalse()
    {
        var database = $"file:complete-receipt-{Guid.NewGuid():N}?mode=memory&cache=shared";
        var connectionString = $"Data Source={database};Default Timeout=10";
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connectionString)
            .Options;
        await using (var seed = new ApplicationDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            await SeedRequiredMasterDataAsync(seed);
            seed.GoodsReceipts.Add(new GoodsReceipt
            {
                Id = 1,
                ReceiptNo = "GR-CONTENTION",
                Status = DocumentStatus.Draft,
                Lines =
                {
                    new GoodsReceiptLine
                    {
                        ProductId = 1,
                        LocationId = 1,
                        LotNo = "LOT-CONTENTION",
                        Qty = 4m,
                        UnitPrice = 2.5m
                    }
                }
            });
            await seed.SaveChangesAsync();
        }

        await using var firstContext = new ApplicationDbContext(options);
        await using var secondContext = new ApplicationDbContext(options);
        await firstContext.GoodsReceipts.Include(receipt => receipt.Lines).SingleAsync();
        await secondContext.GoodsReceipts.Include(receipt => receipt.Lines).SingleAsync();
        var results = await Task.WhenAll(
            new InventoryService(firstContext).CompleteGoodsReceiptAsync(1, "first"),
            new InventoryService(secondContext).CompleteGoodsReceiptAsync(1, "second"));

        Assert.Equal(new[] { false, true }, results.OrderBy(result => result).ToArray());
        await using var verify = new ApplicationDbContext(options);
        Assert.Equal(DocumentStatus.Completed, (await verify.GoodsReceipts.SingleAsync()).Status);
        Assert.Equal(4m, (await verify.Lots.SingleAsync()).Qty);
        Assert.Equal(4m, (await verify.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Single(await verify.StockTransactions.ToListAsync());
    }

    [Fact]
    public async Task CompleteGoodsIssueAsync_WhenSameDraftIsCompletedConcurrently_OneSucceedsAndOneReturnsFalse()
    {
        var database = $"file:complete-issue-{Guid.NewGuid():N}?mode=memory&cache=shared";
        var connectionString = $"Data Source={database};Default Timeout=10";
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connectionString)
            .Options;
        await using (var seed = new ApplicationDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            await SeedRequiredMasterDataAsync(seed);
            seed.Customers.Add(new Customer { Id = 1, Code = "C", Name = "Customer" });
            seed.Lots.Add(new Lot
            {
                Id = 1,
                ProductId = 1,
                LotNo = "LOT-CONTENTION",
                Qty = 10m,
                UnitPrice = 3m
            });
            seed.StockBalances.Add(new StockBalance
            {
                ProductId = 1,
                LotId = 1,
                LocationId = 1,
                QtyAvailable = 10m
            });
            seed.GoodsIssues.Add(new GoodsIssue
            {
                Id = 1,
                IssueNo = "GI-CONTENTION",
                CustomerId = 1,
                Status = DocumentStatus.Draft,
                Lines =
                {
                    new GoodsIssueLine
                    {
                        ProductId = 1,
                        LotId = 1,
                        LocationId = 1,
                        Qty = 3m
                    }
                }
            });
            await seed.SaveChangesAsync();
        }

        await using var firstContext = new ApplicationDbContext(options);
        await using var secondContext = new ApplicationDbContext(options);
        await firstContext.GoodsIssues.Include(issue => issue.Lines).SingleAsync();
        await secondContext.GoodsIssues.Include(issue => issue.Lines).SingleAsync();
        var results = await Task.WhenAll(
            new InventoryService(firstContext).CompleteGoodsIssueAsync(1, "first"),
            new InventoryService(secondContext).CompleteGoodsIssueAsync(1, "second"));

        Assert.Equal(new[] { false, true }, results.OrderBy(result => result).ToArray());
        await using var verify = new ApplicationDbContext(options);
        Assert.Equal(DocumentStatus.Completed, (await verify.GoodsIssues.SingleAsync()).Status);
        Assert.Equal(7m, (await verify.StockBalances.SingleAsync()).QtyAvailable);
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
        context.Lots.Add(new Lot
        {
            Id = 1,
            ProductId = 1,
            LotNo = "LOT-LEDGER",
            Qty = 5,
            UnitPrice = 4.25m
        });
        context.StockBalances.Add(new StockBalance
        {
            ProductId = 1,
            LotId = 1,
            LocationId = 1,
            QtyAvailable = 5,
            QtyReserved = 4,
            QtyOnHold = 6
        });
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

        var balance = await context.StockBalances.SingleAsync();
        var transaction = await context.StockTransactions.SingleAsync();
        Assert.Equal(17m, balance.QtyAvailable);
        Assert.Equal(4m, balance.QtyReserved);
        Assert.Equal(6m, balance.QtyOnHold);
        Assert.Equal(balance.QtyAvailable, transaction.QtyAfter);
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
        context.StockBalances.Add(new StockBalance
        {
            ProductId = 1,
            LotId = 1,
            LocationId = 1,
            QtyAvailable = 10,
            QtyReserved = 4,
            QtyOnHold = 6
        });
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

        var balance = await context.StockBalances.SingleAsync();
        var transaction = await context.StockTransactions.SingleAsync();
        Assert.Equal(-3m, transaction.Qty);
        Assert.Equal(4m, balance.QtyReserved);
        Assert.Equal(6m, balance.QtyOnHold);
        Assert.Equal(balance.QtyAvailable, transaction.QtyAfter);
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
    public async Task CompleteGoodsReceiptAsync_WhenPostingFails_RestoresExactTrackerStateBeforeLaterSave()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new ThrowOnLedgerInsertInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.GoodsReceipts.Add(new GoodsReceipt
        {
            Id = 1,
            ReceiptNo = "GR-TRACKER-ROLLBACK",
            Status = DocumentStatus.Draft,
            Lines =
            {
                new GoodsReceiptLine
                {
                    ProductId = 1,
                    LocationId = 1,
                    LotNo = "LOT-VALID",
                    Qty = 2m
                },
                new GoodsReceiptLine
                {
                    ProductId = 1,
                    LocationId = 1,
                    LotNo = "LOT-SECOND",
                    Qty = 3m
                }
            }
        });
        context.Suppliers.Add(new Supplier
        {
            Id = 10,
            Code = "SUP-DELETE",
            Name = "Delete later"
        });
        await context.SaveChangesAsync();
        var product = await context.Products.SingleAsync();
        product.Name = "Caller change survives";
        var deletedSupplier = await context.Suppliers.SingleAsync();
        context.Suppliers.Remove(deletedSupplier);
        var addedSupplier = new Supplier
        {
            Code = "SUP-ADD",
            Name = "Add later"
        };
        context.Suppliers.Add(addedSupplier);
        var addedSupplierEntry = context.Entry(addedSupplier);
        var originalTemporaryId = addedSupplier.Id;
        Assert.True(addedSupplierEntry.Property(supplier => supplier.Id).IsTemporary);
        interceptor.Enabled = true;

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            new InventoryService(context).CompleteGoodsReceiptAsync(1, "warehouse"));

        Assert.Equal(EntityState.Modified, context.Entry(product).State);
        Assert.Equal("Caller change survives", product.Name);
        Assert.Equal(EntityState.Added, context.Entry(addedSupplier).State);
        Assert.Equal(originalTemporaryId, addedSupplier.Id);
        Assert.True(context.Entry(addedSupplier)
            .Property(supplier => supplier.Id)
            .IsTemporary);
        Assert.Equal(EntityState.Deleted, context.Entry(deletedSupplier).State);
        Assert.Equal(
            DocumentStatus.Draft,
            (await context.GoodsReceipts.FindAsync(1))!.Status);
        Assert.Empty(context.ChangeTracker.Entries<Lot>()
            .Where(entry => entry.Entity.LotNo.StartsWith("LOT-", StringComparison.Ordinal)));
        Assert.Empty(context.ChangeTracker.Entries<StockBalance>());
        Assert.Empty(context.ChangeTracker.Entries<StockTransaction>());

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal("Caller change survives", (await context.Products.SingleAsync()).Name);
        Assert.Equal("SUP-ADD", (await context.Suppliers.SingleAsync()).Code);
        Assert.Equal(DocumentStatus.Draft, (await context.GoodsReceipts.SingleAsync()).Status);
        Assert.Empty(await context.Lots.ToListAsync());
        Assert.Empty(await context.StockBalances.ToListAsync());
        Assert.Empty(await context.StockTransactions.ToListAsync());
    }

    [Fact]
    public async Task CompleteGoodsIssueAsync_WhenPostingFails_RestoresExactTrackerStateBeforeLaterSave()
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
        context.Locations.Add(new Location
        {
            Id = 2,
            Code = "LOC02",
            Name = "Second location",
            ZoneId = 1
        });
        context.Lots.Add(new Lot
        {
            Id = 1,
            ProductId = 1,
            LotNo = "LOT-TRACKER",
            Qty = 11m,
            UnitPrice = 9m
        });
        context.StockBalances.AddRange(
            new StockBalance
            {
                ProductId = 1,
                LotId = 1,
                LocationId = 1,
                QtyAvailable = 10m
            },
            new StockBalance
            {
                ProductId = 1,
                LotId = 1,
                LocationId = 2,
                QtyAvailable = 1m
            });
        context.GoodsIssues.Add(new GoodsIssue
        {
            Id = 1,
            IssueNo = "GI-TRACKER-ROLLBACK",
            CustomerId = 1,
            Status = DocumentStatus.Draft,
            Lines =
            {
                new GoodsIssueLine
                {
                    ProductId = 1,
                    LotId = 1,
                    LocationId = 1,
                    Qty = 3m
                },
                new GoodsIssueLine
                {
                    ProductId = 1,
                    LotId = 1,
                    LocationId = 2,
                    Qty = 2m
                }
            }
        });
        await context.SaveChangesAsync();
        var product = await context.Products.SingleAsync();
        product.Name = "Caller change survives";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InventoryService(context).CompleteGoodsIssueAsync(1, "warehouse"));

        Assert.Equal(EntityState.Modified, context.Entry(product).State);
        Assert.Equal("Caller change survives", product.Name);
        Assert.Equal(DocumentStatus.Draft, (await context.GoodsIssues.FindAsync(1))!.Status);
        Assert.Empty(context.ChangeTracker.Entries<StockTransaction>());

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal("Caller change survives", (await context.Products.SingleAsync()).Name);
        Assert.Equal(DocumentStatus.Draft, (await context.GoodsIssues.SingleAsync()).Status);
        Assert.Equal(
            new[] { 10m, 1m },
            await context.StockBalances.OrderBy(balance => balance.LocationId)
                .Select(balance => balance.QtyAvailable)
                .ToArrayAsync());
        Assert.Empty(await context.StockTransactions.ToListAsync());
    }

    [Fact]
    public async Task CompleteGoodsIssueAsync_WhenLockedValuationLotDisappears_FailsInsteadOfWritingZero()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new DeleteLotBeforeValuationInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Customers.Add(new Customer { Id = 1, Code = "C", Name = "Customer" });
        context.Lots.Add(new Lot
        {
            Id = 1,
            ProductId = 1,
            LotNo = "LOT-VALUATION-RACE",
            Qty = 5m,
            UnitPrice = 8m
        });
        context.StockBalances.Add(new StockBalance
        {
            ProductId = 1,
            LotId = 1,
            LocationId = 1,
            QtyAvailable = 5m
        });
        context.GoodsIssues.Add(new GoodsIssue
        {
            Id = 1,
            IssueNo = "GI-VALUATION-RACE",
            CustomerId = 1,
            Status = DocumentStatus.Draft,
            Lines =
            {
                new GoodsIssueLine
                {
                    ProductId = 1,
                    LotId = 1,
                    LocationId = 1,
                    Qty = 1m
                }
            }
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        interceptor.Enabled = true;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InventoryService(context).CompleteGoodsIssueAsync(1, "warehouse"));

        Assert.Equal("The issue valuation lot no longer exists.", exception.Message);
        context.ChangeTracker.Clear();
        Assert.Equal(DocumentStatus.Draft, (await context.GoodsIssues.SingleAsync()).Status);
        Assert.Equal(5m, (await context.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Empty(await context.StockTransactions.ToListAsync());
    }

    [Fact]
    public async Task CancelGoodsIssueAsync_WhenValuationLotDisappears_FailsInsteadOfWritingZero()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new DeleteLotBeforeValuationInterceptor(deleteOnLotRead: 1);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Customers.Add(new Customer { Id = 1, Code = "C", Name = "Customer" });
        context.Lots.Add(new Lot
        {
            Id = 1,
            ProductId = 1,
            LotNo = "LOT-CANCEL-VALUATION-RACE",
            Qty = 5m,
            UnitPrice = 8m
        });
        context.StockBalances.Add(new StockBalance
        {
            ProductId = 1,
            LotId = 1,
            LocationId = 1,
            QtyAvailable = 4m
        });
        context.GoodsIssues.Add(new GoodsIssue
        {
            Id = 1,
            IssueNo = "GI-CANCEL-VALUATION-RACE",
            CustomerId = 1,
            Status = DocumentStatus.Completed,
            Lines =
            {
                new GoodsIssueLine
                {
                    ProductId = 1,
                    LotId = 1,
                    LocationId = 1,
                    Qty = 1m
                }
            }
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        interceptor.Enabled = true;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InventoryService(context).CancelGoodsIssueAsync(1, "warehouse"));

        Assert.Equal(
            "The issue cancellation valuation lot no longer exists.",
            exception.Message);
        context.ChangeTracker.Clear();
        Assert.Equal(DocumentStatus.Completed, (await context.GoodsIssues.SingleAsync()).Status);
        Assert.Equal(4m, (await context.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Empty(await context.StockTransactions.ToListAsync());
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
    public async Task CompleteGoodsIssueAsync_LoadsValuationLotBeforeUpdatingBalance()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var commandRecorder = new RecordingCommandInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(commandRecorder)
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Customers.Add(new Customer { Id = 1, Code = "C", Name = "Customer" });
        context.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-ORDER", Qty = 10, UnitPrice = 5 });
        context.StockBalances.Add(new StockBalance
        {
            ProductId = 1,
            LotId = 1,
            LocationId = 1,
            QtyAvailable = 10
        });
        context.GoodsIssues.Add(new GoodsIssue
        {
            Id = 1,
            IssueNo = "GI-ORDER-LOCKS",
            CustomerId = 1,
            Status = DocumentStatus.Draft,
            Lines =
            {
                new GoodsIssueLine { ProductId = 1, LotId = 1, LocationId = 1, Qty = 2 }
            }
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        commandRecorder.Commands.Clear();

        Assert.True(await new InventoryService(context)
            .CompleteGoodsIssueAsync(1, "warehouse"));

        var lotRead = commandRecorder.Commands.FindIndex(command =>
            command.Contains("FROM \"Lots\"", StringComparison.Ordinal));
        var balanceUpdate = commandRecorder.Commands.FindIndex(command =>
            command.Contains("UPDATE \"StockBalances\"", StringComparison.Ordinal));
        Assert.True(lotRead >= 0);
        Assert.True(balanceUpdate > lotRead);
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
    public async Task CompleteGoodsReceiptAsync_OrdersExistingLotsByCanonicalLotNumber()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Lots.AddRange(
            new Lot { Id = 1, ProductId = 1, LotNo = "LOT-Z", Qty = 5, UnitPrice = 2 },
            new Lot { Id = 2, ProductId = 1, LotNo = "LOT-A", Qty = 5, UnitPrice = 3 });
        context.StockBalances.AddRange(
            new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 5 },
            new StockBalance { ProductId = 1, LotId = 2, LocationId = 1, QtyAvailable = 5 });
        context.GoodsReceipts.Add(new GoodsReceipt
        {
            Id = 1,
            ReceiptNo = "GR-EXISTING-LOT-ORDER",
            Status = DocumentStatus.Draft,
            Lines =
            {
                new GoodsReceiptLine
                {
                    ProductId = 1,
                    LotNo = "LOT-A",
                    LocationId = 1,
                    Qty = 1,
                    UnitPrice = 3
                },
                new GoodsReceiptLine
                {
                    ProductId = 1,
                    LotNo = "LOT-Z",
                    LocationId = 1,
                    Qty = 1,
                    UnitPrice = 2
                }
            }
        });
        await context.SaveChangesAsync();

        Assert.True(await new InventoryService(context)
            .CompleteGoodsReceiptAsync(1, "warehouse"));

        context.ChangeTracker.Clear();
        Assert.Equal(
            new[] { 2, 1 },
            await context.StockTransactions
                .OrderBy(transaction => transaction.Id)
                .Select(transaction => transaction.LotId)
                .ToArrayAsync());
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_WithStaleTrackedLotAndBalance_UsesFreshLockedValues()
    {
        var database = $"file:receipt-stale-{Guid.NewGuid():N}?mode=memory&cache=shared";
        await using var keepAlive = new SqliteConnection($"Data Source={database}");
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={database}")
            .Options;
        await using (var seed = new ApplicationDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            await SeedRequiredMasterDataAsync(seed);
            seed.Lots.Add(new Lot
            {
                Id = 1,
                ProductId = 1,
                LotNo = "LOT-STALE-RECEIPT",
                Qty = 5,
                UnitPrice = 2
            });
            seed.StockBalances.Add(new StockBalance
            {
                ProductId = 1,
                LotId = 1,
                LocationId = 1,
                QtyAvailable = 5
            });
            seed.GoodsReceipts.Add(new GoodsReceipt
            {
                Id = 1,
                ReceiptNo = "GR-STALE",
                Status = DocumentStatus.Draft,
                Lines =
                {
                    new GoodsReceiptLine
                    {
                        ProductId = 1,
                        LotNo = "LOT-STALE-RECEIPT",
                        LocationId = 1,
                        Qty = 2,
                        UnitPrice = 6
                    }
                }
            });
            await seed.SaveChangesAsync();
        }

        await using var staleContext = new ApplicationDbContext(options);
        Assert.Equal(5m, (await staleContext.Lots.SingleAsync()).Qty);
        Assert.Equal(5m, (await staleContext.StockBalances.SingleAsync()).QtyAvailable);
        await using (var freshContext = new ApplicationDbContext(options))
        {
            await freshContext.Lots.ExecuteUpdateAsync(setters => setters
                .SetProperty(lot => lot.Qty, 10m)
                .SetProperty(lot => lot.UnitPrice, 4m));
            await freshContext.StockBalances.ExecuteUpdateAsync(setters =>
                setters.SetProperty(balance => balance.QtyAvailable, 8m));
        }

        Assert.True(await new InventoryService(staleContext)
            .CompleteGoodsReceiptAsync(1, "warehouse"));

        staleContext.ChangeTracker.Clear();
        var lot = await staleContext.Lots.SingleAsync();
        Assert.Equal(12m, lot.Qty);
        Assert.Equal(4.33m, lot.UnitPrice);
        Assert.Equal(10m, (await staleContext.StockBalances.SingleAsync()).QtyAvailable);
        var transaction = await staleContext.StockTransactions.SingleAsync();
        Assert.Equal(10m, transaction.QtyAfter);
        Assert.Equal(4.33m, transaction.ValuationRate);
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_WhenExistingBalanceTupleRepeats_PreservesRunningQuantity()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Lots.Add(new Lot
        {
            Id = 1,
            ProductId = 1,
            LotNo = "LOT-REPEATED-RECEIPT",
            Qty = 8,
            UnitPrice = 4
        });
        context.StockBalances.Add(new StockBalance
        {
            ProductId = 1,
            LotId = 1,
            LocationId = 1,
            QtyAvailable = 8
        });
        context.GoodsReceipts.Add(new GoodsReceipt
        {
            Id = 1,
            ReceiptNo = "GR-REPEATED-BALANCE",
            Status = DocumentStatus.Draft,
            Lines =
            {
                new GoodsReceiptLine
                {
                    ProductId = 1,
                    LotNo = "LOT-REPEATED-RECEIPT",
                    LocationId = 1,
                    Qty = 2,
                    UnitPrice = 4
                },
                new GoodsReceiptLine
                {
                    ProductId = 1,
                    LotNo = "LOT-REPEATED-RECEIPT",
                    LocationId = 1,
                    Qty = 3,
                    UnitPrice = 4
                }
            }
        });
        await context.SaveChangesAsync();

        Assert.True(await new InventoryService(context)
            .CompleteGoodsReceiptAsync(1, "warehouse"));

        context.ChangeTracker.Clear();
        Assert.Equal(13m, (await context.Lots.SingleAsync()).Qty);
        Assert.Equal(13m, (await context.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Equal(
            new[] { 10m, 13m },
            await context.StockTransactions
                .OrderBy(transaction => transaction.Id)
                .Select(transaction => transaction.QtyAfter)
                .ToArrayAsync());
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_WhenMissingLotAppearsBeforeLockedRecheck_ReusesItForRepeatedLines()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var insertLot = new InsertLotAfterMissingResolutionInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(insertLot)
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.GoodsReceipts.Add(new GoodsReceipt
        {
            Id = 1,
            ReceiptNo = "GR-MID-RACE-LOT",
            Status = DocumentStatus.Draft,
            Lines =
            {
                new GoodsReceiptLine
                {
                    ProductId = 1,
                    LotNo = InsertLotAfterMissingResolutionInterceptor.LotNo,
                    LocationId = 1,
                    Qty = 2,
                    UnitPrice = 6
                },
                new GoodsReceiptLine
                {
                    ProductId = 1,
                    LotNo = InsertLotAfterMissingResolutionInterceptor.LotNo,
                    LocationId = 1,
                    Qty = 3,
                    UnitPrice = 8
                }
            }
        });
        await context.SaveChangesAsync();
        insertLot.Enabled = true;

        Assert.True(await new InventoryService(context)
            .CompleteGoodsReceiptAsync(1, "warehouse"));

        context.ChangeTracker.Clear();
        var lot = await context.Lots.SingleAsync();
        Assert.Equal(15m, lot.Qty);
        Assert.Equal(5m, (await context.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Equal(
            new[] { 2m, 5m },
            await context.StockTransactions
                .OrderBy(transaction => transaction.Id)
                .Select(transaction => transaction.QtyAfter)
                .ToArrayAsync());
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_WhenEarlierNaturalKeyAppearsAfterResolution_KeepsCanonicalOrder()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var insertLot = new InsertEarlierLotAfterMissingResolutionInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(insertLot)
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-Z", Qty = 10, UnitPrice = 4 });
        context.StockBalances.Add(new StockBalance
        {
            ProductId = 1,
            LotId = 1,
            LocationId = 1,
            QtyAvailable = 10
        });
        context.GoodsReceipts.Add(new GoodsReceipt
        {
            Id = 1,
            ReceiptNo = "GR-MULTI-MID-RACE",
            Status = DocumentStatus.Draft,
            Lines =
            {
                new GoodsReceiptLine
                {
                    ProductId = 1,
                    LotNo = InsertEarlierLotAfterMissingResolutionInterceptor.LotNo,
                    LocationId = 1,
                    Qty = 2,
                    UnitPrice = 5
                },
                new GoodsReceiptLine
                {
                    ProductId = 1,
                    LotNo = "LOT-Z",
                    LocationId = 1,
                    Qty = 2,
                    UnitPrice = 5
                }
            }
        });
        await context.SaveChangesAsync();
        insertLot.Enabled = true;

        Assert.True(await new InventoryService(context)
            .CompleteGoodsReceiptAsync(1, "warehouse"));

        context.ChangeTracker.Clear();
        Assert.Equal(
            new[] { 10, 1 },
            await context.StockTransactions
                .OrderBy(transaction => transaction.Id)
                .Select(transaction => transaction.LotId)
                .ToArrayAsync());
    }

    [Fact]
    public async Task CompleteGoodsIssueAsync_OrdersValuationLotLocksByCanonicalLotNumber()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var commandRecorder = new RecordingCommandInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(commandRecorder)
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Customers.Add(new Customer { Id = 1, Code = "C", Name = "Customer" });
        context.Lots.AddRange(
            new Lot { Id = 1, ProductId = 1, LotNo = "LOT-Z", Qty = 5, UnitPrice = 2 },
            new Lot { Id = 2, ProductId = 1, LotNo = "LOT-A", Qty = 5, UnitPrice = 3 });
        context.StockBalances.AddRange(
            new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 5 },
            new StockBalance { ProductId = 1, LotId = 2, LocationId = 1, QtyAvailable = 5 });
        context.GoodsIssues.Add(new GoodsIssue
        {
            Id = 1,
            IssueNo = "GI-VALUATION-ORDER",
            CustomerId = 1,
            Status = DocumentStatus.Draft,
            Lines =
            {
                new GoodsIssueLine { ProductId = 1, LotId = 1, LocationId = 1, Qty = 1 },
                new GoodsIssueLine { ProductId = 1, LotId = 2, LocationId = 1, Qty = 1 }
            }
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        commandRecorder.Commands.Clear();
        commandRecorder.ParameterValues.Clear();

        Assert.True(await new InventoryService(context)
            .CompleteGoodsIssueAsync(1, "warehouse"));

        var valuationLotReads = commandRecorder.Commands
            .Select((command, index) => (command, index))
            .Where(item =>
                item.command.Contains("FROM \"Lots\"", StringComparison.Ordinal) &&
                item.command.Contains("\"l\".\"UnitPrice\"", StringComparison.Ordinal))
            .Select(item => Convert.ToInt32(
                commandRecorder.ParameterValues[item.index].Single()))
            .TakeLast(2)
            .ToArray();
        Assert.Equal(new[] { 2, 1 }, valuationLotReads);
        Assert.Equal(
            new[] { 2, 1 },
            await context.StockTransactions
                .OrderBy(transaction => transaction.Id)
                .Select(transaction => transaction.LotId)
                .ToArrayAsync());
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
    public async Task CompleteGoodsReceiptAsync_WhenAffectedBalanceIsDirty_RejectsAndPreservesCallerState()
    {
        var database = $"file:complete-receipt-dirty-balance-{Guid.NewGuid():N}?mode=memory&cache=shared";
        await using var keepAlive = new SqliteConnection($"Data Source={database}");
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={database}")
            .Options;
        await using (var seed = new ApplicationDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            await SeedRequiredMasterDataAsync(seed);
            seed.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-DIRTY-BALANCE", Qty = 5 });
            seed.StockBalances.Add(new StockBalance
            {
                ProductId = 1,
                LotId = 1,
                LocationId = 1,
                QtyAvailable = 5
            });
            seed.GoodsReceipts.Add(new GoodsReceipt
            {
                Id = 1,
                ReceiptNo = "GR-DIRTY-BALANCE",
                Status = DocumentStatus.Draft,
                Lines =
                {
                    new GoodsReceiptLine
                    {
                        ProductId = 1,
                        LotNo = "LOT-DIRTY-BALANCE",
                        LocationId = 1,
                        Qty = 2
                    }
                }
            });
            await seed.SaveChangesAsync();
        }

        await using var context = new ApplicationDbContext(options);
        var dirtyBalance = await context.StockBalances.SingleAsync();
        dirtyBalance.QtyReserved = 2;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InventoryService(context).CompleteGoodsReceiptAsync(1, "warehouse"));

        Assert.Equal(EntityState.Modified, context.Entry(dirtyBalance).State);
        Assert.True(context.Entry(dirtyBalance).Property(balance => balance.QtyReserved).IsModified);
        Assert.Equal(2m, dirtyBalance.QtyReserved);
        await using var verify = new ApplicationDbContext(options);
        Assert.Equal(DocumentStatus.Draft, (await verify.GoodsReceipts.SingleAsync()).Status);
        Assert.Equal(5m, (await verify.Lots.SingleAsync()).Qty);
        var databaseBalance = await verify.StockBalances.SingleAsync();
        Assert.Equal(5m, databaseBalance.QtyAvailable);
        Assert.Equal(0m, databaseBalance.QtyReserved);
        Assert.Empty(await verify.StockTransactions.ToListAsync());
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_WhenAffectedLotIsDeleted_RejectsAndPreservesCallerState()
    {
        var database = $"file:complete-receipt-dirty-lot-{Guid.NewGuid():N}?mode=memory&cache=shared";
        await using var keepAlive = new SqliteConnection($"Data Source={database}");
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={database}")
            .Options;
        await using (var seed = new ApplicationDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            await SeedRequiredMasterDataAsync(seed);
            seed.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-DELETED", Qty = 5, UnitPrice = 4 });
            seed.StockBalances.Add(new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 5 });
            seed.GoodsReceipts.Add(new GoodsReceipt
            {
                Id = 1,
                ReceiptNo = "GR-DIRTY-LOT",
                Status = DocumentStatus.Draft,
                Lines =
                {
                    new GoodsReceiptLine
                    {
                        ProductId = 1,
                        LotNo = "LOT-DELETED",
                        LocationId = 1,
                        Qty = 2,
                        UnitPrice = 6
                    }
                }
            });
            await seed.SaveChangesAsync();
        }

        await using var context = new ApplicationDbContext(options);
        var dirtyLot = await context.Lots.SingleAsync();
        context.Lots.Remove(dirtyLot);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InventoryService(context).CompleteGoodsReceiptAsync(1, "warehouse"));

        Assert.Equal(EntityState.Deleted, context.Entry(dirtyLot).State);
        await using var verify = new ApplicationDbContext(options);
        Assert.Equal(DocumentStatus.Draft, (await verify.GoodsReceipts.SingleAsync()).Status);
        Assert.Equal(5m, (await verify.Lots.SingleAsync()).Qty);
        Assert.Equal(5m, (await verify.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Empty(await verify.StockTransactions.ToListAsync());
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_WhenAffectedLotIdentityIsDirty_RejectsAndPreservesCallerState()
    {
        var database = $"file:complete-receipt-dirty-lot-key-{Guid.NewGuid():N}?mode=memory&cache=shared";
        await using var keepAlive = new SqliteConnection($"Data Source={database}");
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={database}")
            .Options;
        await using (var seed = new ApplicationDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            await SeedRequiredMasterDataAsync(seed);
            seed.Products.Add(new Product { Id = 2, Code = "P02", Name = "Product 02", BaseUomId = 1 });
            seed.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-ORIGINAL", Qty = 5, UnitPrice = 4 });
            seed.StockBalances.Add(new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 5 });
            seed.GoodsReceipts.Add(new GoodsReceipt
            {
                Id = 1,
                ReceiptNo = "GR-DIRTY-LOT-KEY",
                Status = DocumentStatus.Draft,
                Lines =
                {
                    new GoodsReceiptLine
                    {
                        ProductId = 1,
                        LotNo = "LOT-ORIGINAL",
                        LocationId = 1,
                        Qty = 2,
                        UnitPrice = 6
                    }
                }
            });
            await seed.SaveChangesAsync();
        }

        await using var context = new ApplicationDbContext(options);
        var dirtyLot = await context.Lots.SingleAsync();
        dirtyLot.ProductId = 2;
        dirtyLot.LotNo = "LOT-LOCAL-EDIT";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InventoryService(context).CompleteGoodsReceiptAsync(1, "warehouse"));

        Assert.Contains("unsaved changes", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(EntityState.Modified, context.Entry(dirtyLot).State);
        Assert.Equal(2, dirtyLot.ProductId);
        Assert.Equal("LOT-LOCAL-EDIT", dirtyLot.LotNo);
        await using var verify = new ApplicationDbContext(options);
        Assert.Equal(DocumentStatus.Draft, (await verify.GoodsReceipts.SingleAsync()).Status);
        var databaseLot = await verify.Lots.SingleAsync();
        Assert.Equal(1, databaseLot.ProductId);
        Assert.Equal("LOT-ORIGINAL", databaseLot.LotNo);
        Assert.Equal(5m, databaseLot.Qty);
        Assert.Equal(5m, (await verify.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Empty(await verify.StockTransactions.ToListAsync());
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_WhenAffectedBalanceIdentityIsDirty_RejectsAndPreservesCallerState()
    {
        var database = $"file:complete-receipt-dirty-balance-key-{Guid.NewGuid():N}?mode=memory&cache=shared";
        await using var keepAlive = new SqliteConnection($"Data Source={database}");
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={database}")
            .Options;
        await using (var seed = new ApplicationDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            await SeedRequiredMasterDataAsync(seed);
            seed.Locations.Add(new Location { Id = 2, Code = "LOC02", Name = "Location 02", ZoneId = 1 });
            seed.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-BALANCE-KEY", Qty = 5 });
            seed.StockBalances.Add(new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 5 });
            seed.GoodsReceipts.Add(new GoodsReceipt
            {
                Id = 1,
                ReceiptNo = "GR-DIRTY-BALANCE-KEY",
                Status = DocumentStatus.Draft,
                Lines =
                {
                    new GoodsReceiptLine
                    {
                        ProductId = 1,
                        LotNo = "LOT-BALANCE-KEY",
                        LocationId = 1,
                        Qty = 2
                    }
                }
            });
            await seed.SaveChangesAsync();
        }

        await using var context = new ApplicationDbContext(options);
        var dirtyBalance = await context.StockBalances.SingleAsync();
        dirtyBalance.LocationId = 2;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InventoryService(context).CompleteGoodsReceiptAsync(1, "warehouse"));

        Assert.Contains("unsaved changes", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(EntityState.Modified, context.Entry(dirtyBalance).State);
        Assert.Equal(2, dirtyBalance.LocationId);
        await using var verify = new ApplicationDbContext(options);
        Assert.Equal(DocumentStatus.Draft, (await verify.GoodsReceipts.SingleAsync()).Status);
        var databaseBalance = await verify.StockBalances.SingleAsync();
        Assert.Equal(1, databaseBalance.LocationId);
        Assert.Equal(5m, databaseBalance.QtyAvailable);
        Assert.Empty(await verify.StockTransactions.ToListAsync());
    }

    [Fact]
    public async Task CompleteGoodsIssueAsync_WhenAffectedTargetsAreDirty_RejectsAndPreservesCallerState()
    {
        var database = $"file:complete-issue-dirty-targets-{Guid.NewGuid():N}?mode=memory&cache=shared";
        await using var keepAlive = new SqliteConnection($"Data Source={database}");
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={database}")
            .Options;
        await using (var seed = new ApplicationDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            await SeedRequiredMasterDataAsync(seed);
            seed.Customers.Add(new Customer { Id = 1, Code = "C", Name = "Customer" });
            seed.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-ISSUE-DIRTY", Qty = 5, UnitPrice = 4 });
            seed.StockBalances.Add(new StockBalance
            {
                ProductId = 1,
                LotId = 1,
                LocationId = 1,
                QtyAvailable = 5
            });
            seed.GoodsIssues.Add(new GoodsIssue
            {
                Id = 1,
                IssueNo = "GI-DIRTY-TARGETS",
                CustomerId = 1,
                Status = DocumentStatus.Draft,
                Lines =
                {
                    new GoodsIssueLine { ProductId = 1, LotId = 1, LocationId = 1, Qty = 2 }
                }
            });
            await seed.SaveChangesAsync();
        }

        await using var context = new ApplicationDbContext(options);
        var dirtyLot = await context.Lots.SingleAsync();
        dirtyLot.UnitPrice = 9;
        var dirtyBalance = await context.StockBalances.SingleAsync();
        dirtyBalance.QtyOnHold = 1;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InventoryService(context).CompleteGoodsIssueAsync(1, "warehouse"));

        Assert.Equal(EntityState.Modified, context.Entry(dirtyLot).State);
        Assert.Equal(9m, dirtyLot.UnitPrice);
        Assert.Equal(EntityState.Modified, context.Entry(dirtyBalance).State);
        Assert.Equal(1m, dirtyBalance.QtyOnHold);
        await using var verify = new ApplicationDbContext(options);
        Assert.Equal(DocumentStatus.Draft, (await verify.GoodsIssues.SingleAsync()).Status);
        Assert.Equal(5m, (await verify.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Equal(4m, (await verify.Lots.SingleAsync()).UnitPrice);
        Assert.Empty(await verify.StockTransactions.ToListAsync());
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_WhenDocumentIsCancelled_ReturnsFalseWithoutReposting()
    {
        await using var context = CreateContext();
        await SeedRequiredMasterDataAsync(context);
        context.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-CANCELLED-RECEIPT", Qty = 5 });
        context.StockBalances.Add(new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 5 });
        context.GoodsReceipts.Add(new GoodsReceipt
        {
            Id = 1,
            ReceiptNo = "GR-CANCELLED",
            Status = DocumentStatus.Cancelled,
            Lines =
            {
                new GoodsReceiptLine
                {
                    ProductId = 1,
                    LotNo = "LOT-CANCELLED-RECEIPT",
                    LocationId = 1,
                    Qty = 2
                }
            }
        });
        await context.SaveChangesAsync();

        Assert.False(await new InventoryService(context)
            .CompleteGoodsReceiptAsync(1, "warehouse"));

        Assert.Equal(DocumentStatus.Cancelled, (await context.GoodsReceipts.SingleAsync()).Status);
        Assert.Equal(5m, (await context.Lots.SingleAsync()).Qty);
        Assert.Equal(5m, (await context.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Empty(await context.StockTransactions.ToListAsync());
    }

    [Fact]
    public async Task CompleteGoodsIssueAsync_WhenDocumentIsCancelled_ReturnsFalseWithoutReposting()
    {
        await using var context = CreateContext();
        await SeedRequiredMasterDataAsync(context);
        context.Customers.Add(new Customer { Id = 1, Code = "C", Name = "Customer" });
        context.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-CANCELLED-ISSUE", Qty = 5 });
        context.StockBalances.Add(new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 5 });
        context.GoodsIssues.Add(new GoodsIssue
        {
            Id = 1,
            IssueNo = "GI-CANCELLED",
            CustomerId = 1,
            Status = DocumentStatus.Cancelled,
            Lines =
            {
                new GoodsIssueLine { ProductId = 1, LotId = 1, LocationId = 1, Qty = 2 }
            }
        });
        await context.SaveChangesAsync();

        Assert.False(await new InventoryService(context)
            .CompleteGoodsIssueAsync(1, "warehouse"));

        Assert.Equal(DocumentStatus.Cancelled, (await context.GoodsIssues.SingleAsync()).Status);
        Assert.Equal(5m, (await context.StockBalances.SingleAsync()).QtyAvailable);
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
    public async Task CancelGoodsReceiptAsync_Success_ReversesStockAndWritesSecondLedgerRow()
    {
        await using var context = CreateContext();
        await SeedRequiredMasterDataAsync(context);
        context.GoodsReceipts.Add(new GoodsReceipt
        {
            Id = 1,
            ReceiptNo = "GR-CANCEL-SUCCESS",
            Status = DocumentStatus.Draft,
            Lines =
            {
                new GoodsReceiptLine
                {
                    ProductId = 1,
                    LocationId = 1,
                    LotNo = "LOT-CANCEL-SUCCESS",
                    Qty = 50,
                    UnitPrice = 10m
                }
            }
        });
        await context.SaveChangesAsync();

        var service = new InventoryService(context);
        Assert.True(await service.CompleteGoodsReceiptAsync(1, "user-1"));

        Assert.True(await service.CancelGoodsReceiptAsync(1, "user-1"));
        context.ChangeTracker.Clear();

        Assert.Equal(DocumentStatus.Cancelled, (await context.GoodsReceipts.FindAsync(1))!.Status);
        Assert.Equal(0m, (await context.StockBalances.SingleAsync()).QtyAvailable);
        var transactions = await context.StockTransactions.OrderBy(transaction => transaction.Id).ToListAsync();
        Assert.Equal(2, transactions.Count);
        Assert.False(transactions[0].IsCancelled);
        Assert.True(transactions[1].IsCancelled);
        Assert.Equal(-50m, transactions[1].Qty);
        Assert.Equal(0m, transactions[1].QtyAfter);
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
            new StockBalance
            {
                ProductId = 1,
                LotId = 1,
                LocationId = 1,
                QtyAvailable = 8,
                QtyReserved = 4,
                QtyOnHold = 6
            },
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
        var targetBalance = await context.StockBalances
            .SingleAsync(balance => balance.LotId == 1);
        Assert.Equal(3m, targetBalance.QtyAvailable);
        Assert.Equal(4m, targetBalance.QtyReserved);
        Assert.Equal(6m, targetBalance.QtyOnHold);
        Assert.Equal(3m, (await context.Lots.FindAsync(1))!.Qty);
        context.ChangeTracker.Clear();
        var persistedBalances = await context.StockBalances
            .OrderBy(balance => balance.LotId)
            .ToListAsync();
        var persistedTarget = persistedBalances.Single(balance => balance.LotId == 1);
        Assert.Equal(
            new[] { 3m, 100m },
            persistedBalances.Select(balance => balance.QtyAvailable).ToArray());
        Assert.Equal(
            new[] { 3m, 100m },
            await context.Lots.OrderBy(lot => lot.Id).Select(lot => lot.Qty).ToArrayAsync());
        var transaction = await context.StockTransactions.SingleAsync();
        Assert.Equal(TransactionType.Receipt, transaction.Type);
        Assert.Equal(1, transaction.LotId);
        Assert.Equal(-5m, transaction.Qty);
        Assert.Equal(persistedTarget.QtyAvailable, transaction.QtyAfter);
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
        context.ChangeTracker.Clear();
        var trackedReceipt = await context.GoodsReceipts.SingleAsync();
        var trackedLines = context.Entry(trackedReceipt).Collection(receipt => receipt.Lines);
        Assert.False(trackedLines.IsLoaded);
        var product = await context.Products.FindAsync(1);
        product!.Name = "Locally edited product";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InventoryService(context).CancelGoodsReceiptAsync(1, "warehouse"));

        Assert.Contains("Cần 5, Hiện có 2", exception.Message);
        Assert.False(trackedLines.IsLoaded);
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
            QtyAvailable = 3,
            QtyReserved = 4,
            QtyOnHold = 6
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
        var balance = await context.StockBalances.SingleAsync();
        Assert.Equal(5m, balance.QtyAvailable);
        Assert.Equal(4m, balance.QtyReserved);
        Assert.Equal(6m, balance.QtyOnHold);
        context.ChangeTracker.Clear();
        var persistedBalance = await context.StockBalances.SingleAsync();
        Assert.Equal(5m, persistedBalance.QtyAvailable);
        Assert.Equal(4m, persistedBalance.QtyReserved);
        Assert.Equal(6m, persistedBalance.QtyOnHold);
        var transaction = await context.StockTransactions.SingleAsync();
        Assert.Equal(TransactionType.Issue, transaction.Type);
        Assert.Equal(2m, transaction.Qty);
        Assert.Equal(persistedBalance.QtyAvailable, transaction.QtyAfter);
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

    [Fact]
    public async Task CancelGoodsIssueAsync_WhenRepeatedKeyBalanceExists_PreservesPerLineRunningBalance()
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
            IssueNo = "GI-REPEATED-BALANCE",
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

        context.ChangeTracker.Clear();
        Assert.Equal(8m, (await context.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Equal(
            new[] { 5m, 8m },
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
    public async Task CancelGoodsReceiptAsync_WhenAmbientCancellationFails_RollsBackOnlyToItsSavepoint()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Lots.AddRange(
            new Lot { Id = 1, ProductId = 1, LotNo = "LOT-A", Qty = 10 },
            new Lot { Id = 2, ProductId = 1, LotNo = "LOT-B", Qty = 2 });
        context.StockBalances.AddRange(
            new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 10 },
            new StockBalance { ProductId = 1, LotId = 2, LocationId = 1, QtyAvailable = 2 });
        context.GoodsReceipts.Add(new GoodsReceipt
        {
            Id = 1,
            ReceiptNo = "GR-AMBIENT-ROLLBACK",
            Status = DocumentStatus.Completed,
            Lines =
            {
                new GoodsReceiptLine { ProductId = 1, LocationId = 1, LotNo = "LOT-A", Qty = 4 },
                new GoodsReceiptLine { ProductId = 1, LocationId = 1, LotNo = "LOT-B", Qty = 5 }
            }
        });
        await context.SaveChangesAsync();
        await using var ambientTransaction = await context.Database.BeginTransactionAsync();
        var product = await context.Products.FindAsync(1);
        product!.Name = "Ambient change survives";
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InventoryService(context).CancelGoodsReceiptAsync(1, "warehouse"));
        await ambientTransaction.CommitAsync();

        context.ChangeTracker.Clear();
        Assert.Equal("Ambient change survives", (await context.Products.SingleAsync()).Name);
        Assert.Equal(DocumentStatus.Completed, (await context.GoodsReceipts.SingleAsync()).Status);
        Assert.Equal(
            new[] { 10m, 2m },
            await context.StockBalances.OrderBy(balance => balance.LotId)
                .Select(balance => balance.QtyAvailable)
                .ToArrayAsync());
        Assert.Equal(
            new[] { 10m, 2m },
            await context.Lots.OrderBy(lot => lot.Id).Select(lot => lot.Qty).ToArrayAsync());
        Assert.Empty(await context.StockTransactions.ToListAsync());
    }

    [Fact]
    public async Task CancelGoodsReceiptAsync_WhenSameDocumentIsCancelledConcurrently_OneSucceedsAndOneReturnsFalse()
    {
        var database = $"file:cancel-same-{Guid.NewGuid():N}?mode=memory&cache=shared";
        var connectionString = $"Data Source={database};Default Timeout=10";
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connectionString)
            .Options;
        await using (var seed = new ApplicationDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            await SeedRequiredMasterDataAsync(seed);
            seed.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-SAME", Qty = 10 });
            seed.StockBalances.Add(new StockBalance
            {
                ProductId = 1,
                LotId = 1,
                LocationId = 1,
                QtyAvailable = 10
            });
            seed.GoodsReceipts.Add(new GoodsReceipt
            {
                Id = 1,
                ReceiptNo = "GR-SAME",
                Status = DocumentStatus.Completed,
                Lines =
                {
                    new GoodsReceiptLine
                    {
                        ProductId = 1,
                        LocationId = 1,
                        LotNo = "LOT-SAME",
                        Qty = 4
                    }
                }
            });
            await seed.SaveChangesAsync();
        }

        await using var firstContext = new ApplicationDbContext(options);
        await using var secondContext = new ApplicationDbContext(options);
        var results = await Task.WhenAll(
            new InventoryService(firstContext).CancelGoodsReceiptAsync(1, "first"),
            new InventoryService(secondContext).CancelGoodsReceiptAsync(1, "second"));

        Assert.Equal(new[] { false, true }, results.OrderBy(result => result).ToArray());
        await using var verify = new ApplicationDbContext(options);
        Assert.Equal(DocumentStatus.Cancelled, (await verify.GoodsReceipts.SingleAsync()).Status);
        Assert.Equal(6m, (await verify.Lots.SingleAsync()).Qty);
        Assert.Equal(6m, (await verify.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Single(await verify.StockTransactions.ToListAsync());
    }

    [Fact]
    public async Task CancelGoodsReceiptAsync_WhenTargetBalanceIsDirty_RejectsWithoutLosingCallerChange()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-DIRTY", Qty = 10 });
        context.StockBalances.Add(new StockBalance
        {
            ProductId = 1,
            LotId = 1,
            LocationId = 1,
            QtyAvailable = 10
        });
        context.GoodsReceipts.Add(new GoodsReceipt
        {
            Id = 1,
            ReceiptNo = "GR-DIRTY-BALANCE",
            Status = DocumentStatus.Completed,
            Lines =
            {
                new GoodsReceiptLine
                {
                    ProductId = 1,
                    LocationId = 1,
                    LotNo = "LOT-DIRTY",
                    Qty = 4
                }
            }
        });
        await context.SaveChangesAsync();
        var balance = await context.StockBalances.SingleAsync();
        balance.QtyReserved = 3;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InventoryService(context).CancelGoodsReceiptAsync(1, "warehouse"));

        Assert.Contains("unsaved changes", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(context.Entry(balance).Property(candidate => candidate.QtyReserved).IsModified);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var persisted = await context.StockBalances.SingleAsync();
        Assert.Equal(10m, persisted.QtyAvailable);
        Assert.Equal(3m, persisted.QtyReserved);
        Assert.Equal(DocumentStatus.Completed, (await context.GoodsReceipts.SingleAsync()).Status);
        Assert.Empty(await context.StockTransactions.ToListAsync());
    }

    [Fact]
    public async Task CancelGoodsReceiptAsync_WhenTargetBalanceIdentityIsDirty_RejectsAndPreservesCallerState()
    {
        var database = $"file:cancel-dirty-balance-key-{Guid.NewGuid():N}?mode=memory&cache=shared";
        await using var keepAlive = new SqliteConnection($"Data Source={database}");
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={database}")
            .Options;
        await using (var seed = new ApplicationDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            await SeedRequiredMasterDataAsync(seed);
            seed.Lots.AddRange(
                new Lot { Id = 1, ProductId = 1, LotNo = "LOT-TARGET", Qty = 5 },
                new Lot { Id = 2, ProductId = 1, LotNo = "LOT-LOCAL", Qty = 5 });
            seed.StockBalances.Add(new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 5 });
            seed.GoodsReceipts.Add(new GoodsReceipt
            {
                Id = 1,
                ReceiptNo = "GR-DIRTY-BALANCE-KEY",
                Status = DocumentStatus.Completed,
                Lines =
                {
                    new GoodsReceiptLine { ProductId = 1, LotNo = "LOT-TARGET", LocationId = 1, Qty = 2 }
                }
            });
            await seed.SaveChangesAsync();
        }

        await using var context = new ApplicationDbContext(options);
        var dirtyBalance = await context.StockBalances.SingleAsync();
        dirtyBalance.LotId = 2;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InventoryService(context).CancelGoodsReceiptAsync(1, "warehouse"));

        Assert.Contains("unsaved changes", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(EntityState.Modified, context.Entry(dirtyBalance).State);
        Assert.Equal(2, dirtyBalance.LotId);
        await using var verify = new ApplicationDbContext(options);
        Assert.Equal(DocumentStatus.Completed, (await verify.GoodsReceipts.SingleAsync()).Status);
        Assert.Equal(5m, (await verify.Lots.SingleAsync(lot => lot.Id == 1)).Qty);
        var databaseBalance = await verify.StockBalances.SingleAsync();
        Assert.Equal(1, databaseBalance.LotId);
        Assert.Equal(5m, databaseBalance.QtyAvailable);
        Assert.Empty(await verify.StockTransactions.ToListAsync());
    }

    [Fact]
    public async Task CancelGoodsReceiptAsync_WhenTrackedDocumentIsModified_RejectsAndPreservesCallerState()
    {
        var database = $"file:cancel-dirty-receipt-{Guid.NewGuid():N}?mode=memory&cache=shared";
        await using var keepAlive = new SqliteConnection($"Data Source={database}");
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={database}")
            .Options;
        await using (var seed = new ApplicationDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            await SeedRequiredMasterDataAsync(seed);
            seed.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-DIRTY-DOC", Qty = 5 });
            seed.StockBalances.Add(new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 5 });
            seed.GoodsReceipts.Add(new GoodsReceipt
            {
                Id = 1,
                ReceiptNo = "GR-DIRTY-DOC",
                Status = DocumentStatus.Completed,
                Lines =
                {
                    new GoodsReceiptLine { ProductId = 1, LotNo = "LOT-DIRTY-DOC", LocationId = 1, Qty = 2 }
                }
            });
            await seed.SaveChangesAsync();
        }

        await using var context = new ApplicationDbContext(options);
        var dirtyReceipt = await context.GoodsReceipts.SingleAsync();
        dirtyReceipt.Status = DocumentStatus.Draft;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InventoryService(context).CancelGoodsReceiptAsync(1, "warehouse"));

        Assert.Equal(EntityState.Modified, context.Entry(dirtyReceipt).State);
        Assert.Equal(DocumentStatus.Draft, dirtyReceipt.Status);
        await using var verify = new ApplicationDbContext(options);
        Assert.Equal(DocumentStatus.Completed, (await verify.GoodsReceipts.SingleAsync()).Status);
        Assert.Equal(5m, (await verify.Lots.SingleAsync()).Qty);
        Assert.Equal(5m, (await verify.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Empty(await verify.StockTransactions.ToListAsync());
    }

    [Fact]
    public async Task CancelGoodsIssueAsync_WhenTrackedDocumentIsDeleted_RejectsAndPreservesCallerState()
    {
        var database = $"file:cancel-dirty-issue-{Guid.NewGuid():N}?mode=memory&cache=shared";
        await using var keepAlive = new SqliteConnection($"Data Source={database}");
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={database}")
            .Options;
        await using (var seed = new ApplicationDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            await SeedRequiredMasterDataAsync(seed);
            seed.Customers.Add(new Customer { Id = 1, Code = "C", Name = "Customer" });
            seed.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-DIRTY-ISSUE-DOC", Qty = 5 });
            seed.StockBalances.Add(new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 3 });
            seed.GoodsIssues.Add(new GoodsIssue
            {
                Id = 1,
                IssueNo = "GI-DIRTY-DOC",
                CustomerId = 1,
                Status = DocumentStatus.Completed,
                Lines =
                {
                    new GoodsIssueLine { ProductId = 1, LotId = 1, LocationId = 1, Qty = 2 }
                }
            });
            await seed.SaveChangesAsync();
        }

        await using var context = new ApplicationDbContext(options);
        var dirtyIssue = await context.GoodsIssues.SingleAsync();
        context.GoodsIssues.Remove(dirtyIssue);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InventoryService(context).CancelGoodsIssueAsync(1, "warehouse"));

        Assert.Equal(EntityState.Deleted, context.Entry(dirtyIssue).State);
        await using var verify = new ApplicationDbContext(options);
        Assert.Equal(DocumentStatus.Completed, (await verify.GoodsIssues.SingleAsync()).Status);
        Assert.Equal(3m, (await verify.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Empty(await verify.StockTransactions.ToListAsync());
    }

    [Fact]
    public async Task CancelGoodsReceiptAsync_WhenTargetLotIsDirtyInAmbientTransaction_PreservesCallerWork()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Lots.Add(new Lot
        {
            Id = 1,
            ProductId = 1,
            LotNo = "LOT-DIRTY",
            Qty = 10,
            UnitPrice = 5
        });
        context.StockBalances.Add(new StockBalance
        {
            ProductId = 1,
            LotId = 1,
            LocationId = 1,
            QtyAvailable = 10
        });
        context.GoodsReceipts.Add(new GoodsReceipt
        {
            Id = 1,
            ReceiptNo = "GR-DIRTY-LOT",
            Status = DocumentStatus.Completed,
            Lines =
            {
                new GoodsReceiptLine
                {
                    ProductId = 1,
                    LocationId = 1,
                    LotNo = "LOT-DIRTY",
                    Qty = 4
                }
            }
        });
        await context.SaveChangesAsync();
        await using var ambientTransaction = await context.Database.BeginTransactionAsync();
        var product = await context.Products.FindAsync(1);
        product!.Name = "Prior ambient work";
        await context.SaveChangesAsync();
        var lot = await context.Lots.FindAsync(1);
        lot!.UnitPrice = 8;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InventoryService(context).CancelGoodsReceiptAsync(1, "warehouse"));

        Assert.Contains("unsaved changes", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(context.Entry(lot).Property(candidate => candidate.UnitPrice).IsModified);
        await context.SaveChangesAsync();
        await ambientTransaction.CommitAsync();
        context.ChangeTracker.Clear();
        Assert.Equal("Prior ambient work", (await context.Products.SingleAsync()).Name);
        Assert.Equal(8m, (await context.Lots.SingleAsync()).UnitPrice);
        Assert.Equal(10m, (await context.Lots.SingleAsync()).Qty);
        Assert.Equal(10m, (await context.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Equal(DocumentStatus.Completed, (await context.GoodsReceipts.SingleAsync()).Status);
        Assert.Empty(await context.StockTransactions.ToListAsync());
    }

    [Fact]
    public async Task CancelGoodsIssueAsync_WhenValuationLotIsDirty_RejectsWithoutLosingCallerChange()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Customers.Add(new Customer { Id = 1, Code = "C", Name = "Customer" });
        context.Lots.Add(new Lot
        {
            Id = 1,
            ProductId = 1,
            LotNo = "LOT-ISSUE-DIRTY",
            Qty = 10,
            UnitPrice = 5
        });
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
            IssueNo = "GI-DIRTY-LOT",
            CustomerId = 1,
            Status = DocumentStatus.Completed,
            Lines =
            {
                new GoodsIssueLine { ProductId = 1, LotId = 1, LocationId = 1, Qty = 2 }
            }
        });
        await context.SaveChangesAsync();
        var lot = await context.Lots.FindAsync(1);
        lot!.UnitPrice = 8;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InventoryService(context).CancelGoodsIssueAsync(1, "warehouse"));

        Assert.Contains("unsaved changes", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(context.Entry(lot).Property(candidate => candidate.UnitPrice).IsModified);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(8m, (await context.Lots.SingleAsync()).UnitPrice);
        Assert.Equal(3m, (await context.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Equal(DocumentStatus.Completed, (await context.GoodsIssues.SingleAsync()).Status);
        Assert.Empty(await context.StockTransactions.ToListAsync());
    }

    [Fact]
    public async Task CancelGoodsIssueAsync_WhenTargetBalanceIdentityIsDirty_RejectsAndPreservesCallerState()
    {
        var database = $"file:cancel-issue-dirty-balance-key-{Guid.NewGuid():N}?mode=memory&cache=shared";
        await using var keepAlive = new SqliteConnection($"Data Source={database}");
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={database}")
            .Options;
        await using (var seed = new ApplicationDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            await SeedRequiredMasterDataAsync(seed);
            seed.Products.Add(new Product { Id = 2, Code = "P02", Name = "Product 02", BaseUomId = 1 });
            seed.Customers.Add(new Customer { Id = 1, Code = "C", Name = "Customer" });
            seed.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-ISSUE-BALANCE-KEY", Qty = 5 });
            seed.StockBalances.Add(new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 3 });
            seed.GoodsIssues.Add(new GoodsIssue
            {
                Id = 1,
                IssueNo = "GI-DIRTY-BALANCE-KEY",
                CustomerId = 1,
                Status = DocumentStatus.Completed,
                Lines =
                {
                    new GoodsIssueLine { ProductId = 1, LotId = 1, LocationId = 1, Qty = 2 }
                }
            });
            await seed.SaveChangesAsync();
        }

        await using var context = new ApplicationDbContext(options);
        var dirtyBalance = await context.StockBalances.SingleAsync();
        dirtyBalance.ProductId = 2;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InventoryService(context).CancelGoodsIssueAsync(1, "warehouse"));

        Assert.Contains("unsaved changes", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(EntityState.Modified, context.Entry(dirtyBalance).State);
        Assert.Equal(2, dirtyBalance.ProductId);
        await using var verify = new ApplicationDbContext(options);
        Assert.Equal(DocumentStatus.Completed, (await verify.GoodsIssues.SingleAsync()).Status);
        var databaseBalance = await verify.StockBalances.SingleAsync();
        Assert.Equal(1, databaseBalance.ProductId);
        Assert.Equal(3m, databaseBalance.QtyAvailable);
        Assert.Empty(await verify.StockTransactions.ToListAsync());
    }

    [Fact]
    public async Task CancelGoodsIssueAsync_WithStaleTrackedBalance_RestoresFromLockedDatabaseQuantity()
    {
        var database = $"file:cancel-stale-{Guid.NewGuid():N}?mode=memory&cache=shared";
        await using var keepAlive = new SqliteConnection($"Data Source={database}");
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={database}")
            .Options;
        await using (var seed = new ApplicationDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            await SeedRequiredMasterDataAsync(seed);
            seed.Customers.Add(new Customer { Id = 1, Code = "C", Name = "Customer" });
            seed.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-STALE", Qty = 10, UnitPrice = 5 });
            seed.StockBalances.Add(new StockBalance
            {
                ProductId = 1,
                LotId = 1,
                LocationId = 1,
                QtyAvailable = 3
            });
            seed.GoodsIssues.Add(new GoodsIssue
            {
                Id = 1,
                IssueNo = "GI-STALE",
                CustomerId = 1,
                Status = DocumentStatus.Completed,
                Lines =
                {
                    new GoodsIssueLine { ProductId = 1, LotId = 1, LocationId = 1, Qty = 2 }
                }
            });
            await seed.SaveChangesAsync();
        }

        await using var staleContext = new ApplicationDbContext(options);
        Assert.Equal(3m, (await staleContext.StockBalances.SingleAsync()).QtyAvailable);
        await using (var freshContext = new ApplicationDbContext(options))
        {
            await freshContext.StockBalances.ExecuteUpdateAsync(setters =>
                setters.SetProperty(balance => balance.QtyAvailable, 7m));
        }

        Assert.True(await new InventoryService(staleContext)
            .CancelGoodsIssueAsync(1, "warehouse"));

        staleContext.ChangeTracker.Clear();
        Assert.Equal(9m, (await staleContext.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Equal(9m, (await staleContext.StockTransactions.SingleAsync()).QtyAfter);
    }

    [Fact]
    public async Task CancelGoodsReceiptAsync_OrdersResolvedLotsByCanonicalLotNumber()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var commandRecorder = new RecordingCommandInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(commandRecorder)
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Lots.AddRange(
            new Lot { Id = 1, ProductId = 1, LotNo = "LOT-Z", Qty = 10 },
            new Lot { Id = 2, ProductId = 1, LotNo = "LOT-A", Qty = 10 });
        context.StockBalances.AddRange(
            new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 10 },
            new StockBalance { ProductId = 1, LotId = 2, LocationId = 1, QtyAvailable = 10 });
        context.GoodsReceipts.Add(new GoodsReceipt
        {
            Id = 1,
            ReceiptNo = "GR-LOCK-ORDER",
            Status = DocumentStatus.Completed,
            Lines =
            {
                new GoodsReceiptLine { ProductId = 1, LocationId = 1, LotNo = "LOT-A", Qty = 1 },
                new GoodsReceiptLine { ProductId = 1, LocationId = 1, LotNo = "LOT-Z", Qty = 1 }
            }
        });
        await context.SaveChangesAsync();
        commandRecorder.Commands.Clear();

        Assert.True(await new InventoryService(context)
            .CancelGoodsReceiptAsync(1, "warehouse"));

        Assert.Equal(
            new[] { "Lot", "Balance", "Lot", "Balance" },
            commandRecorder.Commands
                .Where(command =>
                    command.Contains("UPDATE \"Lots\"", StringComparison.Ordinal) ||
                    command.Contains("UPDATE \"StockBalances\"", StringComparison.Ordinal))
                .Select(command => command.Contains("UPDATE \"Lots\"", StringComparison.Ordinal)
                    ? "Lot"
                    : "Balance")
                .ToArray());
        Assert.Equal(
            new[] { 2, 1 },
            await context.StockTransactions.OrderBy(transaction => transaction.Id)
                .Select(transaction => transaction.LotId)
                .ToArrayAsync());
    }

    [Fact]
    public async Task CancelGoodsIssueAsync_ClaimsDocumentBeforeLoadingValuationLotAndLockingBalance()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var commandRecorder = new RecordingCommandInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(commandRecorder)
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Customers.Add(new Customer { Id = 1, Code = "C", Name = "Customer" });
        context.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-ORDER", Qty = 10, UnitPrice = 5 });
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
            IssueNo = "GI-LOCK-ORDER",
            CustomerId = 1,
            Status = DocumentStatus.Completed,
            Lines =
            {
                new GoodsIssueLine { ProductId = 1, LotId = 1, LocationId = 1, Qty = 2 }
            }
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        commandRecorder.Commands.Clear();

        Assert.True(await new InventoryService(context)
            .CancelGoodsIssueAsync(1, "warehouse"));

        var lotRead = commandRecorder.Commands.FindIndex(command =>
            command.Contains("FROM \"Lots\"", StringComparison.Ordinal));
        var documentClaim = commandRecorder.Commands.FindIndex(command =>
            command.Contains("UPDATE \"GoodsIssues\"", StringComparison.Ordinal));
        var balanceRead = commandRecorder.Commands.FindIndex(command =>
            command.Contains("FROM \"StockBalances\"", StringComparison.Ordinal));
        Assert.True(documentClaim >= 0);
        Assert.True(lotRead > documentClaim);
        Assert.True(balanceRead > lotRead);
    }

    [Fact]
    public async Task CancelGoodsIssueAsync_OrdersBalanceLocksByCanonicalLotNumber()
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
        context.Lots.AddRange(
            new Lot { Id = 1, ProductId = 1, LotNo = "LOT-Z", Qty = 10, UnitPrice = 4 },
            new Lot { Id = 2, ProductId = 1, LotNo = "LOT-A", Qty = 10, UnitPrice = 5 });
        context.StockBalances.AddRange(
            new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 3 },
            new StockBalance { ProductId = 1, LotId = 2, LocationId = 1, QtyAvailable = 3 });
        context.GoodsIssues.Add(new GoodsIssue
        {
            Id = 1,
            IssueNo = "GI-NATURAL-ORDER",
            CustomerId = 1,
            Status = DocumentStatus.Completed,
            Lines =
            {
                new GoodsIssueLine { ProductId = 1, LotId = 1, LocationId = 1, Qty = 2 },
                new GoodsIssueLine { ProductId = 1, LotId = 2, LocationId = 1, Qty = 2 }
            }
        });
        await context.SaveChangesAsync();

        Assert.True(await new InventoryService(context)
            .CancelGoodsIssueAsync(1, "warehouse"));

        Assert.Equal(
            new[] { 2, 1 },
            await context.StockTransactions
                .OrderBy(transaction => transaction.Id)
                .Select(transaction => transaction.LotId)
                .ToArrayAsync());
    }

    [Fact]
    public void CancelGoodsIssueAsync_GeneratesExactSqlServerKeyRangeLockBackedByUniqueIndex()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=CancellationSqlShape;Trusted_Connection=True")
            .Options;
        using var context = new ApplicationDbContext(options);
        var service = new InventoryService(context);
        var method = typeof(InventoryService).GetMethod(
            "CreateSqlServerLockedBalanceQuery",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);
        var query = Assert.IsAssignableFrom<IQueryable<StockBalance>>(
            method!.Invoke(service, new object[] { 11, 22, 33 }));
        var sql = query.ToQueryString();
        Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", sql, StringComparison.Ordinal);
        Assert.Contains("[ProductId] =", sql, StringComparison.Ordinal);
        Assert.Contains("[LotId] =", sql, StringComparison.Ordinal);
        Assert.Contains("[LocationId] =", sql, StringComparison.Ordinal);

        var stockBalanceType = context.Model.FindEntityType(typeof(StockBalance));
        var uniqueTuple = Assert.Single(stockBalanceType!.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[]
                {
                    nameof(StockBalance.ProductId),
                    nameof(StockBalance.LotId),
                    nameof(StockBalance.LocationId)
                }));
        Assert.True(uniqueTuple.IsUnique);
    }

    [Fact]
    public void CompletionFlows_GenerateSqlServerLotQueriesWithExplicitLockSemantics()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=CompletionSqlShape;Trusted_Connection=True")
            .Options;
        using var context = new ApplicationDbContext(options);
        var service = new InventoryService(context);

        var resolveMethod = typeof(InventoryService).GetMethod(
            "CreateSqlServerReceiptLotResolutionQuery",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(resolveMethod);
        var resolveQuery = Assert.IsAssignableFrom<IQueryable<Lot>>(
            resolveMethod!.Invoke(service, new object[] { 11, "LOT-11" }));
        var resolveSql = resolveQuery.ToQueryString();
        Assert.Contains("WITH (READCOMMITTEDLOCK)", resolveSql, StringComparison.Ordinal);
        Assert.Contains("[ProductId] =", resolveSql, StringComparison.Ordinal);
        Assert.Contains("[LotNo] =", resolveSql, StringComparison.Ordinal);

        var resolveByIdMethod = typeof(InventoryService).GetMethod(
            "CreateSqlServerLotResolutionByIdQuery",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(resolveByIdMethod);
        var resolveByIdQuery = Assert.IsAssignableFrom<IQueryable<Lot>>(
            resolveByIdMethod!.Invoke(service, new object[] { 22 }));
        var resolveByIdSql = resolveByIdQuery.ToQueryString();
        Assert.Contains("WITH (READCOMMITTEDLOCK)", resolveByIdSql, StringComparison.Ordinal);
        Assert.Contains("[Id] =", resolveByIdSql, StringComparison.Ordinal);

        var lockedByNaturalKeyMethod = typeof(InventoryService).GetMethod(
            "CreateSqlServerLockedLotByNaturalKeyQuery",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(lockedByNaturalKeyMethod);
        var lockedByNaturalKeyQuery = Assert.IsAssignableFrom<IQueryable<Lot>>(
            lockedByNaturalKeyMethod!.Invoke(service, new object[] { 33, "LOT-33" }));
        var lockedByNaturalKeySql = lockedByNaturalKeyQuery.ToQueryString();
        Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", lockedByNaturalKeySql, StringComparison.Ordinal);
        Assert.Contains("[ProductId] =", lockedByNaturalKeySql, StringComparison.Ordinal);
        Assert.Contains("[LotNo] =", lockedByNaturalKeySql, StringComparison.Ordinal);

        var lotType = context.Model.FindEntityType(typeof(Lot));
        var uniqueLotNumber = Assert.Single(lotType!.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(Lot.LotNo) }));
        Assert.True(uniqueLotNumber.IsUnique);
    }

    [Fact]
    public void CancellationTransactions_UseNormalIsolationAndUniqueSqlServerSafeSavepointNames()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Services",
            "InventoryService.cs"));
        Assert.DoesNotContain(
            "BeginTransactionAsync(IsolationLevel.Serializable)",
            source,
            StringComparison.Ordinal);

        var method = typeof(InventoryService).GetMethod(
            "CreateCancellationSavepointName",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        var first = Assert.IsType<string>(method!.Invoke(null, null));
        var second = Assert.IsType<string>(method.Invoke(null, null));
        Assert.NotEqual(first, second);
        Assert.InRange(first.Length, 1, 32);
        Assert.InRange(second.Length, 1, 32);

        var isolationMethod = typeof(InventoryService).GetMethod(
            "IsUnsupportedSqlServerAmbientIsolation",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(isolationMethod);
        Assert.True(Assert.IsType<bool>(
            isolationMethod!.Invoke(null, new object[] { System.Data.IsolationLevel.Serializable })));
        Assert.True(Assert.IsType<bool>(
            isolationMethod.Invoke(null, new object[] { System.Data.IsolationLevel.RepeatableRead })));
        Assert.True(Assert.IsType<bool>(
            isolationMethod.Invoke(null, new object[] { System.Data.IsolationLevel.ReadUncommitted })));
        Assert.True(Assert.IsType<bool>(
            isolationMethod.Invoke(null, new object[] { System.Data.IsolationLevel.Chaos })));
        Assert.True(Assert.IsType<bool>(
            isolationMethod.Invoke(null, new object[] { System.Data.IsolationLevel.Unspecified })));
        Assert.False(Assert.IsType<bool>(
            isolationMethod.Invoke(null, new object[] { System.Data.IsolationLevel.ReadCommitted })));
        Assert.True(Assert.IsType<bool>(
            isolationMethod.Invoke(null, new object[] { System.Data.IsolationLevel.Snapshot })));

        Assert.Contains(
            "IsUnsupportedSqlServerAmbientIsolation(",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelGoodsIssueAsync_WhenAmbientSaveFails_RollsBackOnlyToItsSavepoint()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Customers.Add(new Customer { Id = 1, Code = "C", Name = "Customer" });
        context.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-ISSUE", Qty = 10 });
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
            IssueNo = "GI-AMBIENT-ROLLBACK",
            CustomerId = 1,
            Status = DocumentStatus.Completed,
            Lines =
            {
                new GoodsIssueLine { ProductId = 1, LotId = 1, LocationId = 1, Qty = 2 }
            }
        });
        await context.SaveChangesAsync();
        await using var ambientTransaction = await context.Database.BeginTransactionAsync();
        var product = await context.Products.FindAsync(1);
        product!.Name = "Ambient issue change survives";
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            new InventoryService(context).CancelGoodsIssueAsync(1, null!));
        await ambientTransaction.CommitAsync();

        context.ChangeTracker.Clear();
        Assert.Equal("Ambient issue change survives", (await context.Products.SingleAsync()).Name);
        Assert.Equal(DocumentStatus.Completed, (await context.GoodsIssues.SingleAsync()).Status);
        Assert.Equal(3m, (await context.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Empty(await context.StockTransactions.ToListAsync());
    }

    [Fact]
    public async Task CancelGoodsIssueAsync_InAmbientTransaction_ReleasesSavepointsOnSuccessAndFalse()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedRequiredMasterDataAsync(context);
        context.Customers.Add(new Customer { Id = 1, Code = "C", Name = "Customer" });
        context.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-ISSUE", Qty = 10 });
        context.StockBalances.Add(new StockBalance
        {
            ProductId = 1,
            LotId = 1,
            LocationId = 1,
            QtyAvailable = 3
        });
        context.GoodsIssues.AddRange(
            new GoodsIssue
            {
                Id = 1,
                IssueNo = "GI-AMBIENT-SUCCESS",
                CustomerId = 1,
                Status = DocumentStatus.Completed,
                Lines =
                {
                    new GoodsIssueLine { ProductId = 1, LotId = 1, LocationId = 1, Qty = 2 }
                }
            },
            new GoodsIssue
            {
                Id = 2,
                IssueNo = "GI-AMBIENT-DRAFT",
                CustomerId = 1,
                Status = DocumentStatus.Draft
            });
        await context.SaveChangesAsync();
        await using var ambientTransaction = await context.Database.BeginTransactionAsync();
        var service = new InventoryService(context);

        Assert.True(await service.CancelGoodsIssueAsync(1, "warehouse"));
        Assert.False(await service.CancelGoodsIssueAsync(2, "warehouse"));
        await ambientTransaction.CommitAsync();

        context.ChangeTracker.Clear();
        Assert.Equal(
            new[] { DocumentStatus.Cancelled, DocumentStatus.Draft },
            await context.GoodsIssues.OrderBy(issue => issue.Id)
                .Select(issue => issue.Status)
                .ToArrayAsync());
        Assert.Equal(5m, (await context.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Single(await context.StockTransactions.ToListAsync());
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
        context.Lots.Add(new Lot
        {
            Id = 1,
            ProductId = 1,
            LotNo = "LOT-001",
            Qty = 10,
            UnitPrice = 6.25m
        });
        context.StockBalances.Add(new StockBalance
        {
            ProductId = 1,
            LotId = 1,
            LocationId = 1,
            QtyAvailable = 0,
            QtyReserved = 4,
            QtyOnHold = 10
        });
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
        Assert.Equal(4, balance.QtyReserved);
        Assert.Equal(0, balance.QtyOnHold);
        var line = await context.StocktakeLines.SingleAsync();
        Assert.Equal(-2, line.QtyDiscrepancy);
        var transaction = await context.StockTransactions.SingleAsync();
        Assert.Equal(TransactionType.Adjust, transaction.Type);
        Assert.Equal(-2, transaction.Qty);
        Assert.Equal(balance.QtyAvailable, transaction.QtyAfter);
        Assert.Equal(6.25m, transaction.ValuationRate);
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WmsMes.sln")))
        {
            directory = directory.Parent;
        }

        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }

    private sealed class RecordingCommandInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public List<IReadOnlyList<object?>> ParameterValues { get; } = [];

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>>
            NonQueryExecutingAsync(
                System.Data.Common.DbCommand command,
                Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
                Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>>
            ReaderExecutingAsync(
                System.Data.Common.DbCommand command,
                Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
                Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result,
                CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        private void Record(System.Data.Common.DbCommand command)
        {
            Commands.Add(command.CommandText);
            ParameterValues.Add(command.Parameters
                .Cast<System.Data.Common.DbParameter>()
                .Select(parameter => parameter.Value)
                .ToArray());
        }
    }

    private sealed class InsertLotAfterMissingResolutionInterceptor :
        Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public const string LotNo = "LOT-MID-RACE";

        public bool Enabled { get; set; }

        public override async ValueTask<System.Data.Common.DbDataReader> ReaderExecutedAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandExecutedEventData eventData,
            System.Data.Common.DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (!Enabled ||
                !command.CommandText.Contains("FROM \"Lots\"", StringComparison.Ordinal) ||
                !command.CommandText.Contains("\"LotNo\"", StringComparison.Ordinal))
            {
                return result;
            }

            Enabled = false;
            await using var insert = command.Connection!.CreateCommand();
            insert.Transaction = command.Transaction;
            insert.CommandText = """
                INSERT INTO "Lots" ("Id", "LotNo", "ProductId", "Qty", "UnitPrice")
                VALUES (10, 'LOT-MID-RACE', 1, 10, 4);
                """;
            await insert.ExecuteNonQueryAsync(cancellationToken);
            return result;
        }
    }

    private sealed class InsertEarlierLotAfterMissingResolutionInterceptor :
        Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public const string LotNo = "LOT-A";

        public bool Enabled { get; set; }

        public override async ValueTask<System.Data.Common.DbDataReader> ReaderExecutedAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandExecutedEventData eventData,
            System.Data.Common.DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (!Enabled ||
                !command.CommandText.Contains("FROM \"Lots\"", StringComparison.Ordinal) ||
                !command.CommandText.Contains("\"LotNo\"", StringComparison.Ordinal) ||
                !command.Parameters.Cast<System.Data.Common.DbParameter>()
                    .Any(parameter => Equals(parameter.Value, LotNo)))
            {
                return result;
            }

            Enabled = false;
            await using var insert = command.Connection!.CreateCommand();
            insert.Transaction = command.Transaction;
            insert.CommandText = """
                INSERT INTO "Lots" ("Id", "LotNo", "ProductId", "Qty", "UnitPrice")
                VALUES (10, 'LOT-A', 1, 10, 4);
                """;
            await insert.ExecuteNonQueryAsync(cancellationToken);
            return result;
        }
    }

    private sealed class DeleteLotBeforeValuationInterceptor :
        Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        private readonly int _deleteOnLotRead;
        private int _lotReads;

        public DeleteLotBeforeValuationInterceptor(int deleteOnLotRead = 2)
        {
            _deleteOnLotRead = deleteOnLotRead;
        }

        public bool Enabled { get; set; }

        public override async ValueTask<
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<
                System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<
                System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (!Enabled ||
                !command.CommandText.Contains("FROM \"Lots\"", StringComparison.Ordinal) ||
                ++_lotReads != _deleteOnLotRead)
            {
                return result;
            }

            Enabled = false;
            await using (var defer = command.Connection!.CreateCommand())
            {
                defer.Transaction = command.Transaction;
                defer.CommandText = "PRAGMA defer_foreign_keys = ON;";
                await defer.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var delete = command.Connection!.CreateCommand())
            {
                delete.Transaction = command.Transaction;
                delete.CommandText = """DELETE FROM "Lots" WHERE "Id" = 1;""";
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            return result;
        }
    }

    private sealed class ThrowOnLedgerInsertInterceptor :
        Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public bool Enabled { get; set; }

        public override ValueTask<
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<
                System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<
                System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled &&
                command.CommandText.Contains(
                    "INSERT INTO \"StockTransactions\"",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Simulated ledger insert failure.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
