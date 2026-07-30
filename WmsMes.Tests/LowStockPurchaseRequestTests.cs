using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public sealed class LowStockPurchaseRequestTests
{
    [Fact]
    public async Task CreateRequestFromLowStock_CreatesOneDraftRequestWithExactEligibleQuantities()
    {
        await using var context = CreateContext();
        await SeedLowStockAsync(
            context,
            (1, "P-LOW", 10m, 50m, 2m),
            (2, "P-BAD", 10m, 2m, 2m),
            (3, "P-NEG", 10m, 1m, 2m));
        var lowStock = new Mock<ILowStockService>(MockBehavior.Strict);
        var utcNow = new DateTimeOffset(2026, 7, 30, 3, 4, 5, TimeSpan.Zero);
        var controller = Controller(context, lowStock.Object, utcNow);

        var result = await controller.CreateRequestFromLowStock();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PurchaseOrderController.Requests), redirect.ActionName);
        var request = await context.PurchaseRequests
            .Include(entity => entity.Items)
            .SingleAsync();
        Assert.StartsWith("PR-LOWSTOCK-", request.RequestNo);
        Assert.Equal(utcNow.UtcDateTime, request.RequestDate);
        Assert.Equal(utcNow.UtcDateTime.AddDays(3), request.RequiredDate);
        Assert.Equal(DocumentStatus.Draft, request.Status);
        var item = Assert.Single(request.Items);
        Assert.Equal(1, item.ProductId);
        Assert.Equal(48m, item.Qty);
        Assert.Contains(request.RequestNo, controller.TempData["StatusMessage"]!.ToString());
        Assert.Contains("bỏ qua 2", controller.TempData["StatusMessage"]!.ToString());
    }

    [Fact]
    public async Task CreateRequestFromLowStock_WithNoPositiveSuggestion_DoesNotCreateRequest()
    {
        await using var context = CreateContext();
        await SeedLowStockAsync(
            context,
            (2, "P-BAD", 10m, 2m, 2m));
        var lowStock = new Mock<ILowStockService>(MockBehavior.Strict);
        var controller = Controller(
            context,
            lowStock.Object,
            new DateTimeOffset(2026, 7, 30, 3, 4, 5, TimeSpan.Zero));

        var result = await controller.CreateRequestFromLowStock();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PurchaseOrderController.Requests), redirect.ActionName);
        Assert.Empty(await context.PurchaseRequests.ToListAsync());
        Assert.Contains("không có", controller.TempData["ErrorMessage"]!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateRequestFromLowStock_RepeatedPost_ReturnsSingleOpenDraftBatch()
    {
        await using var context = CreateContext();
        context.Products.Add(new Product
        {
            Id = 1,
            Code = "P-LOW",
            Name = "P-LOW name",
            IsActive = true,
            MinStock = 10,
            MaxStock = 50
        });
        await context.SaveChangesAsync();
        var lowStock = new Mock<ILowStockService>();
        var utcNow = new DateTimeOffset(2026, 7, 30, 3, 4, 5, TimeSpan.Zero);
        var first = Controller(context, lowStock.Object, utcNow);
        var second = Controller(context, lowStock.Object, utcNow);

        await first.CreateRequestFromLowStock();
        await second.CreateRequestFromLowStock();

        var request = Assert.Single(await context.PurchaseRequests
            .Include(entity => entity.Items)
            .ToListAsync());
        Assert.Equal(DocumentStatus.Draft, request.Status);
        Assert.Single(request.Items);
        Assert.NotNull(second.TempData["StatusMessage"]);
    }

    [Fact]
    public void CreateRequestFromLowStock_HasPostAntiforgeryAndBusinessRoleProtection()
    {
        var action = typeof(PurchaseOrderController).GetMethod(
            nameof(PurchaseOrderController.CreateRequestFromLowStock),
            BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(action);
        Assert.Single(action!.GetCustomAttributes<HttpPostAttribute>());
        Assert.Single(action.GetCustomAttributes<ValidateAntiForgeryTokenAttribute>());
        var authorize = Assert.Single(
            typeof(PurchaseOrderController).GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal("Admin,Manager,Planner", authorize.Roles);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task SeedLowStockAsync(
        ApplicationDbContext context,
        params (int Id, string Code, decimal Min, decimal Max, decimal Available)[] items)
    {
        var unit = new UnitOfMeasure { Id = 1, Code = "EA", Name = "Each" };
        var warehouse = new Warehouse { Id = 1, Code = "WH", Name = "Warehouse" };
        var location = new Location
        {
            Id = 1,
            Code = "LOC",
            Name = "Location",
            Zone = new Zone
            {
                Id = 1,
                Code = "ZONE",
                Name = "Zone",
                Warehouse = warehouse
            }
        };
        foreach (var item in items)
        {
            var product = new Product
            {
                Id = item.Id,
                Code = item.Code,
                Name = $"{item.Code} name",
                BaseUom = unit,
                IsActive = true,
                MinStock = item.Min,
                MaxStock = item.Max
            };
            context.StockBalances.Add(new StockBalance
            {
                Product = product,
                Lot = new Lot
                {
                    Id = item.Id,
                    LotNo = $"LOT-{item.Code}",
                    Product = product
                },
                Location = location,
                QtyAvailable = item.Available
            });
        }

        await context.SaveChangesAsync();
    }

    private static PurchaseOrderController Controller(
        ApplicationDbContext context,
        ILowStockService lowStockService,
        DateTimeOffset utcNow)
    {
        var controller = new PurchaseOrderController(
            context,
            Mock.Of<IPurchaseRequestService>(),
            Mock.Of<IPurchaseOrderService>(),
            lowStockService,
            new FixedTimeProvider(utcNow))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.TempData = new TempDataDictionary(
            controller.HttpContext,
            Mock.Of<ITempDataProvider>());
        return controller;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
