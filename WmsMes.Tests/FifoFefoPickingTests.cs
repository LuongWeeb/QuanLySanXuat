using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.DTOs;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public class FifoFefoPickingTests
{
    [Fact]
    public async Task GetPickingRecommendations_ReturnsServiceRecommendations()
    {
        await using var context = CreateContext();
        var expected = new List<PickingRecommendationDto> { new() { LotNo = "LOT-001", RecommendedQty = 5 } };
        var service = new Mock<IInventoryService>();
        service.Setup(item => item.GetPickingRecommendationsAsync(1, 5, PickingStrategy.FEFO)).ReturnsAsync(expected);
        var controller = new InventoryController(context, service.Object);

        var result = await controller.GetPickingRecommendations(1, 5, PickingStrategy.FEFO);

        Assert.Same(expected, Assert.IsType<OkObjectResult>(result).Value);
        service.Verify(item => item.GetPickingRecommendationsAsync(1, 5, PickingStrategy.FEFO), Times.Once);
    }

    [Fact]
    public async Task GetPickingRecommendations_FEFO_ReturnsEarliestExpiringLotFirstAndSplitsRequiredQuantity()
    {
        await using var context = CreateContext();
        var product = Product();
        context.StockBalances.AddRange(
            Balance(product, "LOT-OCT", new DateTime(2026, 10, 1), new DateTime(2026, 5, 1), 10, "FG-01"),
            Balance(product, "LOT-AUG", new DateTime(2026, 8, 1), new DateTime(2026, 6, 1), 10, "FG-02"));
        await context.SaveChangesAsync();

        var recommendations = await new InventoryService(context)
            .GetPickingRecommendationsAsync(1, 15, PickingStrategy.FEFO);

        Assert.Collection(recommendations,
            recommendation =>
            {
                Assert.Equal("LOT-AUG", recommendation.LotNo);
                Assert.Equal(10, recommendation.AvailableQty);
                Assert.Equal(10, recommendation.RecommendedQty);
            },
            recommendation =>
            {
                Assert.Equal("LOT-OCT", recommendation.LotNo);
                Assert.Equal(10, recommendation.AvailableQty);
                Assert.Equal(5, recommendation.RecommendedQty);
            });
    }

    [Fact]
    public async Task GetPickingRecommendations_FIFO_ReturnsEarliestManufacturedLotFirst()
    {
        await using var context = CreateContext();
        var product = Product();
        context.StockBalances.AddRange(
            Balance(product, "NEWER", new DateTime(2026, 8, 1), new DateTime(2026, 6, 1), 10, "FG-01"),
            Balance(product, "OLDER", new DateTime(2026, 10, 1), new DateTime(2026, 4, 1), 10, "FG-02"));
        await context.SaveChangesAsync();

        var recommendations = await new InventoryService(context)
            .GetPickingRecommendationsAsync(1, 20, PickingStrategy.FIFO);

        Assert.Equal(new[] { "OLDER", "NEWER" }, recommendations.Select(recommendation => recommendation.LotNo));
    }

    [Fact]
    public async Task GetPickingRecommendations_FEFO_UsesManufactureDateTieBreakerAndPlacesLotsWithoutExpiryLast()
    {
        await using var context = CreateContext();
        var product = Product();
        context.StockBalances.AddRange(
            Balance(product, "LATER-MANUFACTURED", new DateTime(2026, 8, 1), new DateTime(2026, 6, 1), 10, "FG-01"),
            Balance(product, "EARLIER-MANUFACTURED", new DateTime(2026, 8, 1), new DateTime(2026, 4, 1), 10, "FG-02"),
            Balance(product, "NO-EXPIRY", null, new DateTime(2026, 1, 1), 10, "FG-03"));
        await context.SaveChangesAsync();

        var recommendations = await new InventoryService(context)
            .GetPickingRecommendationsAsync(1, 30, PickingStrategy.FEFO);

        Assert.Equal(new[] { "EARLIER-MANUFACTURED", "LATER-MANUFACTURED", "NO-EXPIRY" }, recommendations.Select(recommendation => recommendation.LotNo));
    }

    [Fact]
    public async Task GetPickingRecommendations_FIFO_UsesStockBalanceIdTieBreaker()
    {
        await using var context = CreateContext();
        var product = Product();
        context.StockBalances.AddRange(
            Balance(product, "HIGHER-ID", new DateTime(2026, 10, 1), new DateTime(2026, 4, 1), 10, "FG-01", 20),
            Balance(product, "LOWER-ID", new DateTime(2026, 8, 1), new DateTime(2026, 4, 1), 10, "FG-02", 10));
        await context.SaveChangesAsync();

        var recommendations = await new InventoryService(context)
            .GetPickingRecommendationsAsync(1, 20, PickingStrategy.FIFO);

        Assert.Equal(new[] { "LOWER-ID", "HIGHER-ID" }, recommendations.Select(recommendation => recommendation.LotNo));
    }

    [Fact]
    public async Task GetPickingRecommendations_ExcludesQuarantineLocation()
    {
        await using var context = CreateContext();
        var product = Product();
        context.StockBalances.AddRange(
            Balance(product, "QUARANTINED", new DateTime(2026, 8, 1), new DateTime(2026, 4, 1), 10, QcService.QuarantineLocationCode),
            Balance(product, "AVAILABLE", new DateTime(2026, 10, 1), new DateTime(2026, 5, 1), 10, "FG-01"));
        await context.SaveChangesAsync();

        var recommendations = await new InventoryService(context)
            .GetPickingRecommendationsAsync(1, 10, PickingStrategy.FEFO);

        var recommendation = Assert.Single(recommendations);
        Assert.Equal("AVAILABLE", recommendation.LotNo);
        Assert.Equal("FG-01", recommendation.LocationCode);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static Product Product() => new() { Id = 1, Code = "P-001", Name = "Product 001" };

    private static StockBalance Balance(Product product, string lotNo, DateTime? expiryDate, DateTime manufactureDate, decimal qty, string locationCode, int? id = null) =>
        new()
        {
            Id = id ?? 0,
            Product = product,
            Lot = new Lot { LotNo = lotNo, ExpiryDate = expiryDate, ManufactureDate = manufactureDate },
            Location = new Location { Code = locationCode, Name = locationCode, Zone = new Zone { Code = $"Z-{locationCode}", Name = "Zone" } },
            QtyAvailable = qty
        };
}
