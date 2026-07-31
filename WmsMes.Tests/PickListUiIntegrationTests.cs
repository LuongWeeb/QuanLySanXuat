using System.Net;
using System.Collections;
using ClosedXML.Excel;
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
using WmsMes.Web.ViewModels;

namespace WmsMes.Tests;

public class PickListUiIntegrationTests : IClassFixture<InventoryCancellationWebApplicationFactory>
{
    private readonly InventoryCancellationWebApplicationFactory _factory;

    public PickListUiIntegrationTests(InventoryCancellationWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/PickList")]
    [InlineData("/PickList/Create")]
    [InlineData("/PickList/Details/999")]
    public async Task PickListPages_RejectUnauthenticatedAndUnauthorizedUsers(string route)
    {
        using var anonymous = _factory.CreateInventoryClient();
        using var unauthorized = _factory.CreateInventoryClient("Viewer");

        var anonymousResponse = await anonymous.GetAsync(route);
        var unauthorizedResponse = await unauthorized.GetAsync(route);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedResponse.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedLayout_RendersDynamicUnreadBadgeAndRecentNotificationLinks()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.AppNotifications.AddRange(
                new AppNotification
                {
                    Title = "Tồn kho thấp",
                    Message = "SKU-LOW cần bổ sung.",
                    Severity = "Warning",
                    CreatedAt = new DateTime(2026, 7, 31, 8, 0, 0, DateTimeKind.Utc),
                    ReferenceUrl = "/Inventory"
                },
                new AppNotification
                {
                    Title = "Đơn hoàn thành",
                    Message = "WO-001 đã hoàn thành.",
                    Severity = "Info",
                    CreatedAt = new DateTime(2026, 7, 31, 9, 0, 0, DateTimeKind.Utc)
                });
            await context.SaveChangesAsync();
        }

        using var client = _factory.CreateInventoryClient("Warehouse");
        var response = await client.GetAsync("/");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"unread-count\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">2</span>", html, StringComparison.Ordinal);
        Assert.Contains("Tồn kho thấp", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/Inventory\"", html, StringComparison.Ordinal);
        Assert.Contains("Đơn hoàn thành", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreatePost_ReturnsValidationViewWithoutCallingServiceWhenNoSalesOrderWasChosen()
    {
        await using var context = CreateContext();
        var service = new StubPickListService();
        var controller = new PickListController(context, service);

        var result = await controller.Create(0);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Null(view.Model);
        Assert.True(controller.ModelState.ContainsKey("salesOrderId"));
        Assert.Empty(service.SalesOrderIds);
    }

    [Fact]
    public async Task CreatePost_RedirectsToNewPickListDetailsWhenServiceCreatesIt()
    {
        await using var context = CreateContext();
        var service = new StubPickListService
        {
            Result = new PickList { Id = 72, PickListNo = "PK-20260731-072", SalesOrderId = 9 }
        };
        var controller = new PickListController(context, service)
        {
            TempData = CreateTempData()
        };

        var result = await controller.Create(9);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal(72, redirect.RouteValues!["id"]);
        Assert.Equal([9], service.SalesOrderIds);
        Assert.Equal("Đã tạo danh sách lấy hàng PK-20260731-072.", controller.TempData["StatusMessage"]);
    }

    [Fact]
    public async Task CreatePost_ReturnsValidationViewWhenSalesOrderDisappearsBeforeCreation()
    {
        await using var context = CreateContext();
        var service = new StubPickListService();
        var controller = new PickListController(context, service);

        var result = await controller.Create(404);

        Assert.IsType<ViewResult>(result);
        Assert.Equal([404], service.SalesOrderIds);
        Assert.Contains(controller.ModelState["salesOrderId"]!.Errors,
            error => error.ErrorMessage.Contains("không tồn tại", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Details_ReturnsNotFoundForUnknownPickList()
    {
        await using var context = CreateContext();

        var result = await new PickListController(context, new StubPickListService()).Details(404);

        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData("/PickList", "Danh sách lấy hàng")]
    [InlineData("/PickList/Create", "Chọn đơn bán hàng")]
    [InlineData("/Report/StockValuation", "Xuất Báo cáo Excel")]
    public async Task AuthorizedPages_RenderTheirActionablePrimaryUi(string route, string expectedContent)
    {
        using var client = _factory.CreateInventoryClient("Warehouse");

        var response = await client.GetAsync(route);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedContent, html, StringComparison.Ordinal);
        Assert.Contains("/PickList", html, StringComparison.Ordinal);
        Assert.Contains("/Report/StockValuation", html, StringComparison.Ordinal);
        if (route == "/Report/StockValuation")
        {
            Assert.Contains("href=\"/Report/ExportStockValuationExcel\"", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Layout_UsesLocalSignalRClientAndDynamicNotificationContractWithoutHardcodedUnreadCount()
    {
        var layout = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Views", "Shared", "_Layout.cshtml"));

        Assert.Contains("GetUnreadCountAsync", layout, StringComparison.Ordinal);
        Assert.Contains("GetRecentNotificationsAsync", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"unread-count\">3</span>", layout, StringComparison.Ordinal);
        Assert.Contains("~/lib/microsoft-signalr/8.0.0/signalr.min.js", layout, StringComparison.Ordinal);
        Assert.Contains(".withUrl(\"/notificationHub\")", layout, StringComparison.Ordinal);
        Assert.Contains(".withAutomaticReconnect()", layout, StringComparison.Ordinal);
        Assert.Contains("connection.on(\"ReceiveNotification\"", layout, StringComparison.Ordinal);
        Assert.Contains("notification-live-region", layout, StringComparison.Ordinal);
        Assert.Contains("asp-controller=\"PickList\"", layout, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"StockValuation\"", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void Layout_PlacesLiveRegionBeforeTheNotificationListenerReadsIt()
    {
        var layout = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Views", "Shared", "_Layout.cshtml"));
        var liveRegionMarkup = layout.IndexOf("<div id=\"notification-live-region\"", StringComparison.Ordinal);
        var listenerLookup = layout.IndexOf("document.getElementById(\"notification-live-region\")", StringComparison.Ordinal);

        Assert.True(liveRegionMarkup >= 0);
        Assert.True(listenerLookup >= 0);
        Assert.True(liveRegionMarkup < listenerLookup,
            "The live region must be in the DOM before the listener captures it.");
    }

    [Fact]
    public void ReceiveNotificationListener_RendersUntrustedPayloadAsTextAndOnlyLinksSafeLocalUrls()
    {
        var layout = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Views", "Shared", "_Layout.cshtml"));
        var handlerStart = layout.IndexOf("connection.on(\"ReceiveNotification\"", StringComparison.Ordinal);
        var handlerEnd = layout.IndexOf("let retryAttempt", handlerStart, StringComparison.Ordinal);
        var handler = layout[handlerStart..handlerEnd];

        Assert.Contains("renderNotification(notification || {})", handler, StringComparison.Ordinal);
        Assert.Contains("heading.textContent = notification.title", layout, StringComparison.Ordinal);
        Assert.Contains("message.textContent = notification.message", layout, StringComparison.Ordinal);
        Assert.Contains("isSafeReferenceUrl(notification.referenceUrl)", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", handler, StringComparison.Ordinal);
        Assert.Contains("liveRegion.textContent", handler, StringComparison.Ordinal);
        Assert.Contains("updateBadge(1)", handler, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StockValuationPage_AppliesTheSelectedWarehouseFilter()
    {
        var data = await SeedFilterableStockBalancesAsync();
        using var client = _factory.CreateInventoryClient("Warehouse");

        var response = await client.GetAsync($"/Report/StockValuation?warehouseId={data.IncludedWarehouseId}");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var tableBodyStart = html.IndexOf("<tbody>", StringComparison.OrdinalIgnoreCase);
        var tableBodyEnd = html.IndexOf("</tbody>", tableBodyStart, StringComparison.OrdinalIgnoreCase);
        var tableBody = html[tableBodyStart..tableBodyEnd];

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(data.IncludedSku, tableBody, StringComparison.Ordinal);
        Assert.DoesNotContain(data.ExcludedSku, tableBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StockValuationExport_AppliesTheSelectedWarehouseFilter()
    {
        var data = await SeedFilterableStockBalancesAsync();
        using var client = _factory.CreateInventoryClient("Warehouse");

        using var response = await client.GetAsync($"/Report/ExportStockValuationExcel?warehouseId={data.IncludedWarehouseId}");
        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var worksheet = Assert.Single(workbook.Worksheets);
        var exportedSkus = worksheet.Column(1).CellsUsed()
            .Skip(1)
            .Select(cell => cell.GetString())
            .Where(value => value.StartsWith("SKU-FILTER-", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(data.IncludedSku, exportedSkus);
        Assert.DoesNotContain(data.ExcludedSku, exportedSkus);
    }

    [Fact]
    public async Task StockValuationPage_AppliesTheSelectedProductFilterAndKeepsItOnExportLink()
    {
        var data = await SeedFilterableStockBalancesAsync();
        using var client = _factory.CreateInventoryClient("Warehouse");

        var response = await client.GetAsync($"/Report/StockValuation?productId={data.IncludedProductId}");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var tableBodyStart = html.IndexOf("<tbody>", StringComparison.OrdinalIgnoreCase);
        var tableBodyEnd = html.IndexOf("</tbody>", tableBodyStart, StringComparison.OrdinalIgnoreCase);
        var tableBody = html[tableBodyStart..tableBodyEnd];

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(data.IncludedSku, tableBody, StringComparison.Ordinal);
        Assert.DoesNotContain(data.ExcludedSku, tableBody, StringComparison.Ordinal);
        Assert.Contains($"/Report/ExportStockValuationExcel?productId={data.IncludedProductId}", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StockValuationPage_WithoutFiltersIncludesBalancesFromBothWarehouses()
    {
        var data = await SeedFilterableStockBalancesAsync();
        using var client = _factory.CreateInventoryClient("Warehouse");

        var response = await client.GetAsync("/Report/StockValuation");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var tableBodyStart = html.IndexOf("<tbody>", StringComparison.OrdinalIgnoreCase);
        var tableBodyEnd = html.IndexOf("</tbody>", tableBodyStart, StringComparison.OrdinalIgnoreCase);
        var tableBody = html[tableBodyStart..tableBodyEnd];

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(data.IncludedSku, tableBody, StringComparison.Ordinal);
        Assert.Contains(data.ExcludedSku, tableBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PickListCreate_OffersOnlyActionableSalesOrdersWithoutLoadingTheirItemsIntoTheView()
    {
        await using var context = CreateContext();
        var customer = new Customer { Id = 1, Code = "CUS-PICK-FILTER", Name = "Customer" };
        context.SalesOrders.AddRange(
            new SalesOrder { Id = 1, OrderNo = "SO-ACTIONABLE", Customer = customer, Status = DocumentStatus.Draft, Items = { new SalesOrderItem { Qty = 4m, DeliveredQty = 1m } } },
            new SalesOrder { Id = 2, OrderNo = "SO-COMPLETED", CustomerId = 1, Status = DocumentStatus.Completed, Items = { new SalesOrderItem { Qty = 4m } } },
            new SalesOrder { Id = 3, OrderNo = "SO-CANCELLED", CustomerId = 1, Status = DocumentStatus.Cancelled, Items = { new SalesOrderItem { Qty = 4m } } },
            new SalesOrder { Id = 4, OrderNo = "SO-DELIVERED", CustomerId = 1, Status = DocumentStatus.Draft, Items = { new SalesOrderItem { Qty = 4m, DeliveredQty = 4m } } });
        await context.SaveChangesAsync();
        var controller = new PickListController(context, new StubPickListService());

        var result = await controller.Create();
        var rows = Assert.IsAssignableFrom<IEnumerable<PickListSalesOrderOptionViewModel>>(
            controller.ViewData["SalesOrders"])
            .ToList();

        Assert.IsType<ViewResult>(result);
        Assert.Single(rows);
        Assert.Equal("SO-ACTIONABLE", rows[0].OrderNo);
        Assert.Equal(3m, rows[0].RemainingQuantity);
    }

    private async Task<FilterableStockBalanceData> SeedFilterableStockBalancesAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var includedWarehouse = new Warehouse { Code = $"WH-FILTER-{suffix}-A", Name = "Included warehouse" };
        var excludedWarehouse = new Warehouse { Code = $"WH-FILTER-{suffix}-B", Name = "Excluded warehouse" };
        var includedProduct = new Product { Code = $"SKU-FILTER-{suffix}-A", Name = "Included product", BaseUomId = 1 };
        var excludedProduct = new Product { Code = $"SKU-FILTER-{suffix}-B", Name = "Excluded product", BaseUomId = 1 };
        context.StockBalances.AddRange(
            new StockBalance
            {
                Product = includedProduct,
                Lot = new Lot { Product = includedProduct, LotNo = $"LOT-FILTER-{suffix}-A", UnitPrice = 100m },
                Location = new Location { Code = $"LOC-FILTER-{suffix}-A", Name = "Included location", Zone = new Zone { Code = $"ZONE-FILTER-{suffix}-A", Name = "Included zone", Warehouse = includedWarehouse } },
                QtyAvailable = 2m
            },
            new StockBalance
            {
                Product = excludedProduct,
                Lot = new Lot { Product = excludedProduct, LotNo = $"LOT-FILTER-{suffix}-B", UnitPrice = 200m },
                Location = new Location { Code = $"LOC-FILTER-{suffix}-B", Name = "Excluded location", Zone = new Zone { Code = $"ZONE-FILTER-{suffix}-B", Name = "Excluded zone", Warehouse = excludedWarehouse } },
                QtyAvailable = 3m
            });
        await context.SaveChangesAsync();
        return new FilterableStockBalanceData(includedWarehouse.Id, includedProduct.Id, includedProduct.Code, excludedProduct.Code);
    }

    private static ApplicationDbContext CreateContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static ITempDataDictionary CreateTempData()
    {
        var provider = new Mock<ITempDataProvider>();
        provider.Setup(item => item.LoadTempData(It.IsAny<HttpContext>()))
            .Returns(new Dictionary<string, object>());
        return new TempDataDictionary(new DefaultHttpContext(), provider.Object);
    }

    private sealed class StubPickListService : IPickListService
    {
        public List<int> SalesOrderIds { get; } = [];
        public PickList? Result { get; init; }

        public Task<PickList?> CreatePickListForSalesOrderAsync(int salesOrderId)
        {
            SalesOrderIds.Add(salesOrderId);
            return Task.FromResult(Result);
        }
    }

    private sealed record FilterableStockBalanceData(int IncludedWarehouseId, int IncludedProductId, string IncludedSku, string ExcludedSku);
}
