using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.Reflection;
using WmsMes.Web.Data;
using WmsMes.Web.Data.Migrations;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public sealed class OperationsFinalFixTests
{
    [Theory]
    [InlineData(7, 7, 0)]
    [InlineData(6, 6, -1)]
    public async Task CycleCountApproval_AdjustsAgainstMovementExpectedAtCount(
        decimal countedQty,
        decimal expectedBalanceAfterApproval,
        decimal expectedAdjustment)
    {
        var (keepAlive, options) = await CreateDatabaseAsync();
        await using (keepAlive)
        {
            await SeedCompletedCycleCountAsync(
                options,
                countedQty,
                currentQty: 7,
                movements:
                [
                    new LedgerMovement(
                        new DateTime(2026, 7, 30, 10, 10, 0, DateTimeKind.Utc),
                        -3m,
                        TransactionType.Issue)
                ]);

            await using var context = new ApplicationDbContext(options);
            var service = new CycleCountService(context, new InventoryService(context));

            Assert.True(await service.ApproveAndAdjustStockAsync(1, "manager-1"));

            context.ChangeTracker.Clear();
            Assert.Equal(
                expectedBalanceAfterApproval,
                await context.StockBalances.Select(balance => balance.QtyAvailable).SingleAsync());
            var adjustment = await context.StockTransactions
                .Where(transaction =>
                    transaction.Type == TransactionType.Adjust &&
                    transaction.ReferenceNo == "CC-MOVEMENT")
                .Select(transaction => (decimal?)transaction.Qty)
                .SingleOrDefaultAsync();
            Assert.Equal(
                expectedAdjustment == 0 ? null : expectedAdjustment,
                adjustment);
        }
    }

    [Fact]
    public async Task CycleCountApproval_DoesNotReconcileMovementsAfterCountCutoff()
    {
        var (keepAlive, options) = await CreateDatabaseAsync();
        await using (keepAlive)
        {
            await SeedCompletedCycleCountAsync(
                options,
                countedQty: 7,
                currentQty: 9,
                movements:
                [
                    new LedgerMovement(
                        new DateTime(2026, 7, 30, 10, 10, 0, DateTimeKind.Utc),
                        -3m,
                        TransactionType.Issue),
                    new LedgerMovement(
                        new DateTime(2026, 7, 30, 10, 40, 0, DateTimeKind.Utc),
                        2m,
                        TransactionType.Receipt)
                ]);

            await using var context = new ApplicationDbContext(options);

            Assert.True(await new CycleCountService(context, new InventoryService(context))
                .ApproveAndAdjustStockAsync(1, "manager-1"));

            context.ChangeTracker.Clear();
            Assert.Equal(
                9,
                await context.StockBalances.Select(balance => balance.QtyAvailable).SingleAsync());
            Assert.DoesNotContain(
                await context.StockTransactions.ToListAsync(),
                transaction =>
                    transaction.Type == TransactionType.Adjust &&
                    transaction.ReferenceNo == "CC-MOVEMENT");
        }
    }

    [Fact]
    public async Task CycleCountReconciliation_OriginalAndReversalInsideCutoff_NetToZero()
    {
        await AssertCancellationReconciliationAsync(
            systemQty: 10,
            countedQty: 10,
            currentQty: 10,
            expectedAtCount: 10,
            expectedVariance: 0,
            expectedCurrentAfterApproval: 10,
            movements:
            [
                new LedgerMovement(
                    new DateTime(2026, 7, 30, 10, 10, 0, DateTimeKind.Utc),
                    -3m,
                    TransactionType.Issue),
                new LedgerMovement(
                    new DateTime(2026, 7, 30, 10, 20, 0, DateTimeKind.Utc),
                    3m,
                    TransactionType.Issue,
                    IsCancelled: true)
            ]);
    }

    [Fact]
    public async Task CycleCountReconciliation_ReversalInsideCutoff_WhenOriginalPredatesSnapshot_CountsReversal()
    {
        await AssertCancellationReconciliationAsync(
            systemQty: 7,
            countedQty: 10,
            currentQty: 10,
            expectedAtCount: 10,
            expectedVariance: 0,
            expectedCurrentAfterApproval: 10,
            movements:
            [
                new LedgerMovement(
                    new DateTime(2026, 7, 30, 9, 50, 0, DateTimeKind.Utc),
                    -3m,
                    TransactionType.Issue),
                new LedgerMovement(
                    new DateTime(2026, 7, 30, 10, 20, 0, DateTimeKind.Utc),
                    3m,
                    TransactionType.Issue,
                    IsCancelled: true)
            ]);
    }

    [Fact]
    public async Task CycleCountReconciliation_ReversalAfterCutoff_RemainsInCurrentWithoutChangingCountVariance()
    {
        await AssertCancellationReconciliationAsync(
            systemQty: 10,
            countedQty: 7,
            currentQty: 10,
            expectedAtCount: 7,
            expectedVariance: 0,
            expectedCurrentAfterApproval: 10,
            movements:
            [
                new LedgerMovement(
                    new DateTime(2026, 7, 30, 10, 10, 0, DateTimeKind.Utc),
                    -3m,
                    TransactionType.Issue),
                new LedgerMovement(
                    new DateTime(2026, 7, 30, 10, 40, 0, DateTimeKind.Utc),
                    3m,
                    TransactionType.Issue,
                    IsCancelled: true)
            ]);
    }

    [Fact]
    public async Task AddDiscoveredItem_WithStaleDraftSnapshot_NeverResurrectsCompletedOrder()
    {
        var (keepAlive, options) = await CreateDatabaseAsync();
        await using (keepAlive)
        {
            await SeedDiscoveredItemScenarioAsync(options);
            await using var staleContext = new ApplicationDbContext(options);
            await staleContext.CycleCountOrders
                .Include(order => order.Items)
                .SingleAsync(order => order.Id == 1);

            await using (var completingContext = new ApplicationDbContext(options))
            {
                await completingContext.CycleCountOrders
                    .Where(order => order.Id == 1)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(order => order.Status, "Completed"));
            }

            var added = await new CycleCountService(
                    staleContext,
                    new InventoryService(staleContext))
                .AddDiscoveredItemAsync(1, "LOC-NEW", "LOT-NEW", 4);

            staleContext.ChangeTracker.Clear();
            Assert.False(added);
            Assert.Equal(
                "Completed",
                await staleContext.CycleCountOrders
                    .Where(order => order.Id == 1)
                    .Select(order => order.Status)
                    .SingleAsync());
            Assert.Empty(await staleContext.CycleCountItems.ToListAsync());
        }
    }

    [Fact]
    public async Task AddDiscoveredItem_WithStaleConcurrentRequests_InsertsAtMostOne()
    {
        var (keepAlive, options) = await CreateDatabaseAsync();
        await using (keepAlive)
        {
            await SeedDiscoveredItemScenarioAsync(options);
            await using var firstContext = new ApplicationDbContext(options);
            await using var staleContext = new ApplicationDbContext(options);
            await firstContext.CycleCountOrders
                .Include(order => order.Items)
                .SingleAsync(order => order.Id == 1);
            await staleContext.CycleCountOrders
                .Include(order => order.Items)
                .SingleAsync(order => order.Id == 1);

            var first = await new CycleCountService(
                    firstContext,
                    new InventoryService(firstContext))
                .AddDiscoveredItemAsync(1, "LOC-NEW", "LOT-NEW", 4);
            var stale = await new CycleCountService(
                    staleContext,
                    new InventoryService(staleContext))
                .AddDiscoveredItemAsync(1, "LOC-NEW", "LOT-NEW", 4);

            Assert.True(first);
            Assert.False(stale);
            await using var verify = new ApplicationDbContext(options);
            Assert.Single(await verify.CycleCountItems.ToListAsync());
            Assert.Equal("InProgress", (await verify.CycleCountOrders.SingleAsync()).Status);
        }
    }

    [Fact]
    public void OperationsIntegrityModel_HasUniqueCycleItemAndOpenLowStockIndexes()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\mssqllocaldb;Database=OperationsFinalModel;Trusted_Connection=True;")
            .Options;
        using var context = new ApplicationDbContext(options);

        var cycleItem = context.Model.FindEntityType(typeof(CycleCountItem));
        var cycleIndex = Assert.Single(cycleItem!.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual(
            [
                nameof(CycleCountItem.CycleCountOrderId),
                nameof(CycleCountItem.LocationId),
                nameof(CycleCountItem.LotId)
            ]));
        Assert.True(cycleIndex.IsUnique);

        var purchaseRequest = context.Model.FindEntityType(typeof(PurchaseRequest));
        var lowStockIndex = Assert.Single(purchaseRequest!.GetIndexes(), index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["LowStockBatchKey"]));
        Assert.True(lowStockIndex.IsUnique);
        Assert.Contains("[Status] = 0", lowStockIndex.GetFilter());
    }

    [Fact]
    public void OperationsIntegrityMigration_WasGeneratedWithBothDatabaseInvariants()
    {
        var migration = Directory.GetFiles(
                Path.Combine(ProjectRoot(), "Data", "Migrations"),
                "*_EnforceOperationsIntegrityInvariants.cs")
            .Select(File.ReadAllText)
            .Single();

        Assert.Contains(
            "IX_CycleCountItems_CycleCountOrderId_LocationId_LotId",
            migration);
        Assert.Contains("unique: true", migration);
        Assert.Contains("IX_PurchaseRequests_LowStockBatchKey", migration);
        Assert.Contains("filter: \"[LowStockBatchKey] IS NOT NULL AND [Status] = 0\"", migration);
    }

    [Fact]
    public void OperationsIntegrityMigration_RejectsDuplicateCycleItemsBeforeCreatingUniqueIndex()
    {
        var migration = new EnforceOperationsIntegrityInvariants();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(EnforceOperationsIntegrityInvariants)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);

        var guardIndex = Enumerable.Range(0, builder.Operations.Count)
            .FirstOrDefault(
                index => builder.Operations[index] is SqlOperation,
                -1);
        var uniqueIndex = Enumerable.Range(0, builder.Operations.Count)
            .FirstOrDefault(
                index =>
                    builder.Operations[index] is CreateIndexOperation createIndex &&
                    createIndex.Name ==
                        "IX_CycleCountItems_CycleCountOrderId_LocationId_LotId",
                -1);
        Assert.True(guardIndex >= 0, "A duplicate-data SQL preflight is required.");
        Assert.Equal(
            guardIndex + 1,
            uniqueIndex);

        var sql = Assert.IsType<SqlOperation>(builder.Operations[guardIndex]).Sql;
        Assert.Contains("FROM [CycleCountItems]", sql, StringComparison.Ordinal);
        Assert.Contains("[CycleCountOrderId]", sql, StringComparison.Ordinal);
        Assert.Contains("[LocationId]", sql, StringComparison.Ordinal);
        Assert.Contains("[LotId]", sql, StringComparison.Ordinal);
        Assert.Contains("HAVING COUNT_BIG(*) > 1", sql, StringComparison.Ordinal);
        Assert.Contains("THROW", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "resolve duplicate CycleCountItems",
            sql,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("before retry", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertCancellationReconciliationAsync(
        decimal systemQty,
        decimal countedQty,
        decimal currentQty,
        decimal expectedAtCount,
        decimal expectedVariance,
        decimal expectedCurrentAfterApproval,
        IReadOnlyList<LedgerMovement> movements)
    {
        var (keepAlive, options) = await CreateDatabaseAsync();
        await using (keepAlive)
        {
            await SeedCompletedCycleCountAsync(
                options,
                countedQty,
                currentQty,
                movements,
                systemQty);
            await using var context = new ApplicationDbContext(options);
            var order = await context.CycleCountOrders
                .AsNoTracking()
                .Include(candidate => candidate.Items)
                .SingleAsync();

            await CycleCountReconciliation.PopulateExpectedAtCountQuantitiesAsync(
                context,
                order);

            var item = Assert.Single(order.Items);
            Assert.Equal(expectedAtCount, item.ExpectedAtCountQty);
            Assert.Equal(expectedVariance, item.AuthoritativeVarianceQty);
            Assert.True(await new CycleCountService(
                    context,
                    new InventoryService(context))
                .ApproveAndAdjustStockAsync(order.Id, "manager-1"));
            context.ChangeTracker.Clear();
            Assert.Equal(
                expectedCurrentAfterApproval,
                await context.StockBalances
                    .Select(balance => balance.QtyAvailable)
                    .SingleAsync());
        }
    }

    private static async Task SeedCompletedCycleCountAsync(
        DbContextOptions<ApplicationDbContext> options,
        decimal countedQty,
        decimal currentQty,
        IReadOnlyList<LedgerMovement> movements,
        decimal systemQty = 10)
    {
        await using var context = new ApplicationDbContext(options);
        var unit = new UnitOfMeasure { Id = 1, Code = "EA", Name = "Each" };
        var warehouse = new Warehouse { Id = 1, Code = "WH", Name = "Warehouse" };
        var zone = new Zone { Id = 1, Code = "ZONE", Name = "Zone", Warehouse = warehouse };
        var location = new Location { Id = 1, Code = "LOC", Name = "Location", Zone = zone };
        var product = new Product
        {
            Id = 1,
            Code = "P",
            Name = "Product",
            BaseUom = unit
        };
        var lot = new Lot
        {
            Id = 1,
            LotNo = "LOT",
            Product = product,
            UnitPrice = 25
        };
        context.StockBalances.Add(new StockBalance
        {
            Id = 1,
            Product = product,
            Lot = lot,
            Location = location,
            QtyAvailable = currentQty
        });
        context.CycleCountOrders.Add(new CycleCountOrder
        {
            Id = 1,
            CountNumber = "CC-MOVEMENT",
            Warehouse = warehouse,
            Status = "Completed",
            CreatedAt = new DateTime(2026, 7, 30, 10, 0, 0, DateTimeKind.Utc),
            CompletedAt = new DateTime(2026, 7, 30, 10, 30, 0, DateTimeKind.Utc),
            CreatedBy = "counter-1",
            Items =
            {
                new CycleCountItem
                {
                    Id = 1,
                    Product = product,
                    Lot = lot,
                    Location = location,
                    SystemQty = systemQty,
                    CountedQty = countedQty
                }
            }
        });
        foreach (var movement in movements)
        {
            context.StockTransactions.Add(new StockTransaction
            {
                Type = movement.Type,
                Product = product,
                Lot = lot,
                Location = location,
                Qty = movement.Qty,
                QtyAfter = 0,
                ValuationRate = lot.UnitPrice,
                IsCancelled = movement.IsCancelled,
                TransactionDate = movement.Timestamp,
                UserId = "warehouse-1",
                ReferenceNo = $"MOVE-{movement.Timestamp:HHmm}"
            });
        }

        await context.SaveChangesAsync();
    }

    private static async Task SeedDiscoveredItemScenarioAsync(
        DbContextOptions<ApplicationDbContext> options)
    {
        await using var context = new ApplicationDbContext(options);
        var unit = new UnitOfMeasure { Id = 1, Code = "EA", Name = "Each" };
        var warehouse = new Warehouse { Id = 1, Code = "WH", Name = "Warehouse" };
        context.Locations.Add(new Location
        {
            Id = 1,
            Code = "LOC-NEW",
            Name = "New location",
            Zone = new Zone
            {
                Id = 1,
                Code = "ZONE",
                Name = "Zone",
                Warehouse = warehouse
            }
        });
        context.Lots.Add(new Lot
        {
            Id = 1,
            LotNo = "LOT-NEW",
            Product = new Product
            {
                Id = 1,
                Code = "P",
                Name = "Product",
                BaseUom = unit
            },
            UnitPrice = 25
        });
        context.CycleCountOrders.Add(new CycleCountOrder
        {
            Id = 1,
            CountNumber = "CC-DISCOVERED",
            Warehouse = warehouse,
            Status = "Draft",
            CreatedBy = "counter-1"
        });
        await context.SaveChangesAsync();
    }

    private static async Task<(SqliteConnection KeepAlive, DbContextOptions<ApplicationDbContext> Options)>
        CreateDatabaseAsync()
    {
        var database = $"file:operations-final-{Guid.NewGuid():N}?mode=memory&cache=shared";
        var keepAlive = new SqliteConnection($"Data Source={database}");
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={database}")
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return (keepAlive, options);
    }

    private static string ProjectRoot() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private readonly record struct LedgerMovement(
        DateTime Timestamp,
        decimal Qty,
        TransactionType Type,
        bool IsCancelled = false);
}
