using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Services;
using WmsMes.Web.ViewModels;

namespace WmsMes.Tests;

public sealed class LowStockPresentationTests
{
    [Fact]
    public async Task DashboardAndInventoryIndex_DeliverTheSameLowStockAlertContract()
    {
        var expected = new[]
        {
            new LowStockItemViewModel
            {
                ProductId = 7,
                ProductCode = "P-007",
                ProductName = "Part 007",
                TotalAvailable = 2,
                MinStock = 10,
                MaxStock = 50
            }
        };
        var lowStock = new Mock<ILowStockService>(MockBehavior.Strict);
        lowStock.Setup(service => service.GetLowStockItemsAsync(default))
            .ReturnsAsync(expected);
        var dashboard = new DashboardController(
            Mock.Of<IOeeService>(),
            TimeProvider.System,
            TimeZoneInfo.Utc,
            lowStock.Object);
        await using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var inventory = new InventoryController(
            context,
            Mock.Of<IReportExportService>(),
            lowStockService: lowStock.Object);

        var dashboardResult = Assert.IsType<ViewResult>(await dashboard.Index());
        var inventoryResult = Assert.IsType<ViewResult>(await inventory.Index());

        var dashboardModel = Assert.IsType<DashboardViewModel>(dashboardResult.Model);
        var inventoryModel = Assert.IsType<InventoryIndexViewModel>(inventoryResult.Model);
        Assert.Same(expected, dashboardModel.LowStockItems);
        Assert.Same(expected, inventoryModel.LowStockItems);
        lowStock.Verify(
            service => service.GetLowStockItemsAsync(default),
            Times.Exactly(2));
    }

    [Theory]
    [InlineData("Dashboard")]
    [InlineData("Inventory")]
    public void IndexView_RendersAccessibleRoleGatedLowStockWidget(string viewFolder)
    {
        var view = File.ReadAllText(Path.Combine(
            ProjectRoot(),
            "Views",
            viewFolder,
            "Index.cshtml"));
        var widget = File.ReadAllText(Path.Combine(
            ProjectRoot(),
            "Views",
            "Shared",
            "_LowStockAlert.cshtml"));

        Assert.Contains("<partial name=\"_LowStockAlert\" model=\"Model.LowStockItems\" />", view);
        Assert.Contains("Model.Any()", widget);
        Assert.Contains("role=\"alert\"", widget);
        Assert.Contains("aria-labelledby=\"low-stock-alert-title\"", widget);
        Assert.Contains("table-responsive", widget);
        Assert.Contains("Mã sản phẩm", widget);
        Assert.Contains("Tên sản phẩm", widget);
        Assert.Contains("Khả dụng", widget);
        Assert.Contains("Tồn tối thiểu", widget);
        Assert.Contains("Tồn tối đa", widget);
        Assert.Contains("Đề xuất mua", widget);
        Assert.Contains("ToVietnameseNumber()", widget);
        Assert.Contains("User.IsInRole(\"Admin\")", widget);
        Assert.Contains("User.IsInRole(\"Manager\")", widget);
        Assert.Contains("User.IsInRole(\"Planner\")", widget);
        Assert.Contains("asp-controller=\"PurchaseOrder\"", widget);
        Assert.Contains("asp-action=\"CreateRequestFromLowStock\"", widget);
        Assert.Contains("method=\"post\"", widget);
        Assert.Contains("asp-antiforgery=\"true\"", widget);
        Assert.Contains("Tạo Yêu cầu Mua hàng tự động (PR)", widget);
        Assert.Contains("item.SuggestedQty > 0", widget);
        Assert.Contains("Cấu hình MaxStock không hợp lệ", widget);
    }

    private static string ProjectRoot() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
