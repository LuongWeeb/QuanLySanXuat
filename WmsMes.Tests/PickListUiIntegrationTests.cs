using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Services;

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
}
