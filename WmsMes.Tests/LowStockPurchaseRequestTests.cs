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
using WmsMes.Web.ViewModels;

namespace WmsMes.Tests;

public sealed class LowStockPurchaseRequestTests
{
    [Fact]
    public async Task CreateRequestFromLowStock_CreatesOneDraftRequestWithExactEligibleQuantities()
    {
        await using var context = CreateContext();
        var lowStock = new Mock<ILowStockService>(MockBehavior.Strict);
        lowStock.Setup(service => service.GetLowStockItemsAsync(default))
            .ReturnsAsync(
            [
                Item(1, "P-LOW", min: 10, max: 50, available: 2),
                Item(2, "P-BAD", min: 10, max: 2, available: 2),
                Item(3, "P-NEG", min: 10, max: 1, available: 2)
            ]);
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
        lowStock.VerifyAll();
    }

    [Fact]
    public async Task CreateRequestFromLowStock_WithNoPositiveSuggestion_DoesNotCreateRequest()
    {
        await using var context = CreateContext();
        var lowStock = new Mock<ILowStockService>(MockBehavior.Strict);
        lowStock.Setup(service => service.GetLowStockItemsAsync(default))
            .ReturnsAsync([Item(2, "P-BAD", min: 10, max: 2, available: 2)]);
        var controller = Controller(
            context,
            lowStock.Object,
            new DateTimeOffset(2026, 7, 30, 3, 4, 5, TimeSpan.Zero));

        var result = await controller.CreateRequestFromLowStock();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PurchaseOrderController.Requests), redirect.ActionName);
        Assert.Empty(await context.PurchaseRequests.ToListAsync());
        Assert.Contains("không có", controller.TempData["ErrorMessage"]!.ToString(), StringComparison.OrdinalIgnoreCase);
        lowStock.VerifyAll();
    }

    [Fact]
    public async Task CreateRequestFromLowStock_SameUtcInstant_GeneratesDistinctRequestNumbers()
    {
        await using var context = CreateContext();
        var lowStock = new Mock<ILowStockService>();
        lowStock.Setup(service => service.GetLowStockItemsAsync(default))
            .ReturnsAsync([Item(1, "P-LOW", min: 10, max: 50, available: 2)]);
        var utcNow = new DateTimeOffset(2026, 7, 30, 3, 4, 5, TimeSpan.Zero);
        var first = Controller(context, lowStock.Object, utcNow);
        var second = Controller(context, lowStock.Object, utcNow);

        await first.CreateRequestFromLowStock();
        await second.CreateRequestFromLowStock();

        var requestNumbers = await context.PurchaseRequests
            .Select(request => request.RequestNo)
            .ToListAsync();
        Assert.Equal(2, requestNumbers.Distinct().Count());
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

    private static LowStockItemViewModel Item(
        int productId,
        string code,
        decimal min,
        decimal max,
        decimal available) =>
        new()
        {
            ProductId = productId,
            ProductCode = code,
            ProductName = $"{code} name",
            MinStock = min,
            MaxStock = max,
            TotalAvailable = available
        };

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
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
