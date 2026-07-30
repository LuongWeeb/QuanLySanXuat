using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public sealed class OperationsEnhancementsTests :
    IClassFixture<InventoryCancellationWebApplicationFactory>
{
    private readonly InventoryCancellationWebApplicationFactory _factory;

    public OperationsEnhancementsTests(InventoryCancellationWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PrintStocktakePdf_ReturnsValidPdfBytes()
    {
        const int countId = 901;
        const string countNumber = "CC-ACCEPT-001";
        await SeedCycleCountAsync(countId, countNumber);

        using var client = _factory.CreateInventoryClient("Warehouse");
        using var response = await client.GetAsync($"/api/print/cyclecount/{countId}");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.True(bytes.Length > 0);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(
            $"BienBanKiemKe_{countNumber}.pdf",
            response.Content.Headers.ContentDisposition?.FileNameStar);
    }

    [Fact]
    public async Task CreateRequestFromLowStock_CreatesPrForLowStockProducts()
    {
        await using var context = CreateContext();
        var product = new Product
        {
            Code = "P-LOW-ACCEPT",
            Name = "Low stock acceptance product",
            BaseUomId = 1,
            MinStock = 10m,
            MaxStock = 50m,
            IsActive = true
        };
        var location = new Location
        {
            Code = "LOC-ACCEPT",
            Name = "Acceptance location",
            Zone = new Zone { Code = "ZONE-ACCEPT", Name = "Acceptance zone" }
        };
        var firstLot = new Lot { LotNo = "LOT-ACCEPT-1", Product = product };
        var secondLot = new Lot { LotNo = "LOT-ACCEPT-2", Product = product };
        context.StockBalances.AddRange(
            new StockBalance
            {
                Product = product,
                Lot = firstLot,
                Location = location,
                QtyAvailable = 1.25m
            },
            new StockBalance
            {
                Product = product,
                Lot = secondLot,
                Location = location,
                QtyAvailable = 0.75m
            });
        await context.SaveChangesAsync();

        var controller = CreatePurchaseOrderController(context);

        var result = await controller.CreateRequestFromLowStock();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PurchaseOrderController.Requests), redirect.ActionName);
        var request = await context.PurchaseRequests
            .Include(entity => entity.Items)
            .SingleAsync();
        Assert.Equal(DocumentStatus.Draft, request.Status);
        var item = Assert.Single(request.Items);
        Assert.Equal(product.Id, item.ProductId);
        Assert.Equal(48m, item.Qty);
    }

    private async Task SeedCycleCountAsync(int id, string countNumber)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var warehouse = new Warehouse { Code = "WH-ACCEPT", Name = "Acceptance warehouse" };
        var location = new Location
        {
            Code = "LOC-PRINT-ACCEPT",
            Name = "Acceptance print location",
            Zone = new Zone
            {
                Code = "ZONE-PRINT-ACCEPT",
                Name = "Acceptance print zone",
                Warehouse = warehouse
            }
        };
        var product = new Product
        {
            Code = "P-PRINT-ACCEPT",
            Name = "Acceptance print product",
            BaseUomId = 1
        };
        var lot = new Lot
        {
            LotNo = "LOT-PRINT-ACCEPT",
            Product = product,
            UnitPrice = 100m
        };
        context.CycleCountOrders.Add(new CycleCountOrder
        {
            Id = id,
            CountNumber = countNumber,
            Warehouse = warehouse,
            CreatedAt = new DateTime(2026, 7, 30),
            CompletedAt = new DateTime(2026, 7, 30),
            CreatedBy = "Acceptance user",
            Items =
            [
                new CycleCountItem
                {
                    Product = product,
                    Location = location,
                    Lot = lot,
                    SystemQty = 10m,
                    CountedQty = 8m,
                    ReasonNote = "Acceptance variance reason"
                }
            ]
        });
        await context.SaveChangesAsync();
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static PurchaseOrderController CreatePurchaseOrderController(
        ApplicationDbContext context)
    {
        var controller = new PurchaseOrderController(
            context,
            Mock.Of<IPurchaseRequestService>(),
            Mock.Of<IPurchaseOrderService>(),
            new LowStockService(context),
            TimeProvider.System)
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
}
