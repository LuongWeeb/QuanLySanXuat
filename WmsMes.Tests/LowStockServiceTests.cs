using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public sealed class LowStockServiceTests
{
    [Fact]
    public async Task GetLowStockItemsAsync_AggregatesAllBalancesAndIncludesZeroBalanceProducts()
    {
        await using var fixture = await EfFixture.CreateAsync();
        var unit = new UnitOfMeasure { Code = "EA", Name = "Each" };
        var location = new Location
        {
            Code = "LOC",
            Name = "Location",
            Zone = new Zone
            {
                Code = "ZONE",
                Name = "Zone",
                Warehouse = new Warehouse { Code = "WH", Name = "Warehouse" }
            }
        };
        var aggregated = Product("P-AGG", min: 10, max: 50, unit);
        var empty = Product("P-ZERO", min: 5, max: 20, unit);
        fixture.Context.StockBalances.AddRange(
            Balance(aggregated, location, "LOT-1", 3.25m),
            Balance(aggregated, location, "LOT-2", -1.25m));
        fixture.Context.Products.Add(empty);
        await fixture.Context.SaveChangesAsync();

        var items = await new LowStockService(fixture.Context).GetLowStockItemsAsync();

        Assert.Collection(
            items,
            item =>
            {
                Assert.Equal("P-AGG", item.ProductCode);
                Assert.Equal(2m, item.TotalAvailable);
                Assert.Equal(10m, item.MinStock);
                Assert.Equal(50m, item.MaxStock);
                Assert.Equal(48m, item.SuggestedQty);
            },
            item =>
            {
                Assert.Equal("P-ZERO", item.ProductCode);
                Assert.Equal(0m, item.TotalAvailable);
                Assert.Equal(20m, item.SuggestedQty);
            });
    }

    [Fact]
    public async Task GetLowStockItemsAsync_ExcludesInactiveZeroMinimumAndNotLowStockProducts()
    {
        await using var fixture = await EfFixture.CreateAsync();
        var unit = new UnitOfMeasure { Code = "EA", Name = "Each" };
        var location = new Location
        {
            Code = "LOC",
            Name = "Location",
            Zone = new Zone
            {
                Code = "ZONE",
                Name = "Zone",
                Warehouse = new Warehouse { Code = "WH", Name = "Warehouse" }
            }
        };
        var inactive = Product("INACTIVE", 10, 50, unit, isActive: false);
        var zeroMinimum = Product("ZERO-MIN", 0, 50, unit);
        var sufficient = Product("ENOUGH", 10, 50, unit);
        var malformed = Product("BAD-MAX", 10, 1, unit);
        fixture.Context.Products.AddRange(inactive, zeroMinimum);
        fixture.Context.StockBalances.AddRange(
            Balance(sufficient, location, "LOT-ENOUGH", 10),
            Balance(malformed, location, "LOT-BAD-MAX", 2));
        await fixture.Context.SaveChangesAsync();

        var items = await new LowStockService(fixture.Context).GetLowStockItemsAsync();

        var item = Assert.Single(items);
        Assert.Equal("BAD-MAX", item.ProductCode);
        Assert.Equal(-1m, item.SuggestedQty);
    }

    [Fact]
    public void Query_UsesSqlServerGroupSumLowStockFilterAndOrdering()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\mssqllocaldb;Database=LowStockQueryShape;Trusted_Connection=True;")
            .Options;
        using var context = new ApplicationDbContext(options);

        var sql = LowStockQuery.Create(context).ToQueryString();

        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SUM(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("QtyAvailable", sql, StringComparison.Ordinal);
        Assert.Contains("COALESCE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HAVING", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MinStock", sql, StringComparison.Ordinal);
        Assert.Contains("TotalAvailable", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Code", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(" AS float", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" AS real", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static Product Product(
        string code,
        decimal min,
        decimal max,
        UnitOfMeasure unit,
        bool isActive = true) =>
        new()
        {
            Code = code,
            Name = $"{code} name",
            BaseUom = unit,
            MinStock = min,
            MaxStock = max,
            IsActive = isActive
        };

    private static StockBalance Balance(
        Product product,
        Location location,
        string lotNo,
        decimal available) =>
        new()
        {
            Product = product,
            Location = location,
            Lot = new Lot { Product = product, LotNo = lotNo },
            QtyAvailable = available
        };

    private sealed class EfFixture : IAsyncDisposable
    {
        private EfFixture(ApplicationDbContext context)
        {
            Context = context;
        }

        public ApplicationDbContext Context { get; }

        public static async Task<EfFixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var context = new ApplicationDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new EfFixture(context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
        }
    }
}
