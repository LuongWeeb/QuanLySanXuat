using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.ViewModels;
using WmsMes.Web.Hubs;

namespace WmsMes.Tests;

public class HomeControllerTests
{
    [Fact]
    public async Task Index_ReturnsAuthoritativeDashboardMetrics()
    {
        await using var context = CreateContext();
        SeedDashboard(context);
        await context.SaveChangesAsync();

        var result = await Controller(context).Index();

        var model = Assert.IsType<DashboardViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(2, model.ActiveWorkOrders);
        Assert.Equal(2, model.PendingQcLots);
        Assert.Equal(38m, model.InventoryVolume);
    }

    [Fact]
    public async Task Metrics_ReturnsSameAuthoritativeDashboardMetrics()
    {
        await using var context = CreateContext();
        SeedDashboard(context);
        await context.SaveChangesAsync();

        var result = await Controller(context).Metrics();

        var metrics = Assert.IsType<DashboardViewModel>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal((2, 2, 38m), (metrics.ActiveWorkOrders, metrics.PendingQcLots, metrics.InventoryVolume));
    }

    [Fact]
    public async Task Search_RedirectsToDashboard_WhenQueryIsBlank()
    {
        await using var context = CreateContext();

        var result = await Controller(context).Search("   ");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(HomeController.Index), redirect.ActionName);
    }

    [Fact]
    public async Task Search_ReturnsTrimmedQueryAndMatchingResultsForEachGroup()
    {
        await using var context = CreateContext();
        var product = new Product { Id = 1, Code = "SKU-MATCH", Name = "Sản phẩm tìm kiếm" };
        context.Products.Add(product);
        context.WorkOrders.Add(new WorkOrder
        {
            Id = 1, Code = "WO-MATCH", Product = product, Qty = 1, DueDate = DateTime.UtcNow,
            BomVersion = "1", RoutingVersion = "1"
        });
        context.Lots.Add(new Lot { Id = 1, LotNo = "LOT-MATCH", Product = product });
        context.Locations.Add(new Location { Id = 1, Code = "LOC-MATCH", Name = "Vị trí", ZoneId = 1 });
        await context.SaveChangesAsync();

        var controller = Controller(context);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.Role, "Manager") }, "Test"))
            }
        };

        var result = await controller.Search(" MATCH ");

        var model = Assert.IsType<SearchResultViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("MATCH", model.Query);
        Assert.Equal("SKU-MATCH", Assert.Single(model.Products).Code);
        Assert.Equal("WO-MATCH", Assert.Single(model.WorkOrders).Code);
        Assert.Equal("LOT-MATCH", Assert.Single(model.Lots).LotNo);
        Assert.Equal("SKU-MATCH", Assert.Single(model.Lots).Product!.Code);
        Assert.Equal("LOC-MATCH", Assert.Single(model.Locations).Code);
    }

    [Fact]
    public async Task Search_LimitsEachResultGroupToTenRecords()
    {
        await using var context = CreateContext();
        for (var index = 1; index <= 11; index++)
        {
            context.Products.Add(new Product { Id = index, Code = $"SKU-MATCH-{index}", Name = "Sản phẩm" });
            context.WorkOrders.Add(new WorkOrder
            {
                Id = index, Code = $"WO-MATCH-{index}", ProductId = index, Qty = 1, DueDate = DateTime.UtcNow,
                BomVersion = "1", RoutingVersion = "1"
            });
            context.Lots.Add(new Lot { Id = index, LotNo = $"LOT-MATCH-{index}", ProductId = index });
            context.Locations.Add(new Location { Id = index, Code = $"LOC-MATCH-{index}", Name = "Vị trí", ZoneId = 1 });
        }
        await context.SaveChangesAsync();

        var controller = Controller(context);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.Role, "Manager") }, "Test"))
            }
        };

        var result = await controller.Search("MATCH");

        var model = Assert.IsType<SearchResultViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(10, model.Products.Count);
        Assert.Equal(10, model.WorkOrders.Count);
        Assert.Equal(10, model.Lots.Count);
        Assert.Equal(10, model.Locations.Count);
    }

    [Fact]
    public async Task Search_HidesWorkOrdersFromUsersWithoutProductionRoles()
    {
        await using var context = CreateContext();
        context.WorkOrders.Add(new WorkOrder
        {
            Id = 1, Code = "WO-MATCH", ProductId = 1, Qty = 1, DueDate = DateTime.UtcNow,
            BomVersion = "1", RoutingVersion = "1"
        });
        await context.SaveChangesAsync();
        var controller = Controller(context);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.Role, "Warehouse") }, "Test"))
            }
        };

        var result = await controller.Search("MATCH");

        var model = Assert.IsType<SearchResultViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Empty(model.WorkOrders);
    }

    [Fact]
    public void SearchView_GroupsResultsAndLinksToRelevantScreens()
    {
        var view = File.ReadAllText(Path.Combine(ProjectRoot(), "Views", "Home", "Search.cshtml"));

        Assert.Contains("Sản phẩm", view);
        Assert.Contains("Lệnh sản xuất", view);
        Assert.Contains("Lô", view);
        Assert.Contains("Vị trí", view);
        Assert.Contains("asp-controller=\"Product\"", view);
        Assert.Contains("asp-controller=\"WorkOrder\"", view);
        Assert.Contains("asp-controller=\"Traceability\"", view);
        Assert.Contains("asp-controller=\"Warehouse\"", view);
        Assert.Contains("Không tìm thấy kết quả", view);
    }

    [Fact]
    public void Layout_RendersGlobalSearchForm()
    {
        var layout = File.ReadAllText(Path.Combine(ProjectRoot(), "Views", "Shared", "_Layout.cshtml"));

        Assert.Contains("asp-controller=\"Home\" asp-action=\"Search\"", layout);
        Assert.Contains("name=\"q\"", layout);
        Assert.Contains("placeholder=\"Tìm SKU, lệnh SX, số lô...\"", layout);
        Assert.Contains("required", layout);
        Assert.Contains("Tìm", layout);
        Assert.Contains("max-width: 250px", layout);
    }

    [Fact]
    public void DashboardView_UsesExactHubRoutesAndEventsWithMetricsRefresh()
    {
        var view = File.ReadAllText(Path.Combine(ProjectRoot(), "Views", "Home", "Index.cshtml"));

        Assert.Contains("/productionHub", view);
        Assert.Contains("/inventoryHub", view);
        Assert.Contains("ReceiveProgressUpdate", view);
        Assert.Contains("ReceiveStockUpdate", view);
        Assert.Contains("@Url.Action(\"Metrics\", \"Home\")", view);
        Assert.Contains("withAutomaticReconnect", view);
        Assert.DoesNotContain("location.reload", view);
    }

    [Fact]
    public void DashboardView_RendersOeeAndLowStockMetricCards()
    {
        var view = File.ReadAllText(Path.Combine(ProjectRoot(), "Views", "Home", "Index.cshtml"));

        Assert.Contains("aria-label=\"Chỉ số OEE Sản xuất\"", view);
        Assert.Contains("id=\"overallOee\"", view);
        Assert.Contains("@Model.OverallOeePercent.ToVietnameseNumber(\"N1\")%", view);
        Assert.Contains("id=\"oeeAvailability\"", view);
        Assert.Contains("@Model.OeeAvailabilityPercent.ToVietnameseNumber(\"N1\")%", view);
        Assert.Contains("id=\"oeePerformance\"", view);
        Assert.Contains("@Model.OeePerformancePercent.ToVietnameseNumber(\"N1\")%", view);
        Assert.Contains("id=\"oeeQuality\"", view);
        Assert.Contains("@Model.OeeQualityPercent.ToVietnameseNumber(\"N1\")%", view);
        Assert.Contains("id=\"lowStockAlertCount\"", view);
        Assert.Contains("@Model.LowStockAlertCount.ToVietnameseNumber()", view);
    }

    [Fact]
    public void DashboardView_RendersAccessibleProductionInventoryAndQualityCharts()
    {
        var view = File.ReadAllText(Path.Combine(ProjectRoot(), "Views", "Home", "Index.cshtml"));

        Assert.Contains("id=\"productionChart\"", view);
        Assert.Contains("aria-label=\"Sản lượng sản xuất 7 ngày gần nhất\"", view);
        Assert.Contains("id=\"inventoryZoneChart\"", view);
        Assert.Contains("aria-label=\"Phân bổ tồn kho theo khu vực\"", view);
        Assert.Contains("id=\"qualityChart\"", view);
        Assert.Contains("aria-label=\"Phân bổ chất lượng Đạt, Tạm giữ và Cách ly\"", view);
    }

    [Fact]
    public void DashboardView_InitializesAndRefreshesAllChartsWithSafelySerializedMetrics()
    {
        var view = File.ReadAllText(Path.Combine(ProjectRoot(), "Views", "Home", "Index.cshtml"));

        Assert.Contains("<script src=\"https://cdn.jsdelivr.net/npm/chart.js@4.4.9/dist/chart.umd.min.js\"></script>", view);
        Assert.DoesNotContain("<script src=\"https://cdn.jsdelivr.net/npm/chart.js\"></script>", view);
        Assert.Contains("JsonSerializer.Serialize(Model.DailyLabels)", view);
        Assert.Contains("JsonSerializer.Serialize(Model.DailyPlannedOutput)", view);
        Assert.Contains("JsonSerializer.Serialize(Model.DailyActualOutput)", view);
        Assert.Contains("JsonSerializer.Serialize(Model.ZoneLabels)", view);
        Assert.Contains("JsonSerializer.Serialize(Model.ZoneQuantities)", view);
        Assert.DoesNotContain("string.Join", view);
        Assert.Contains("productionChart = new window.Chart", view);
        Assert.Contains("inventoryZoneChart = new window.Chart", view);
        Assert.Contains("qualityChart = new window.Chart", view);
        Assert.Contains("metrics.oeeAvailabilityPercent", view);
        Assert.Contains("metrics.lowStockAlertCount", view);
        Assert.Contains("metrics.dailyPlannedOutput", view);
        Assert.Contains("metrics.zoneQuantities", view);
        Assert.Contains("metrics.passedQcCount", view);
        Assert.Contains("productionChart.update()", view);
        Assert.Contains("inventoryZoneChart.update()", view);
        Assert.Contains("qualityChart.update()", view);
    }

    [Fact]
    public void DashboardView_ContinuesRealtimeSetupWhenChartJsIsUnavailable()
    {
        var view = File.ReadAllText(Path.Combine(ProjectRoot(), "Views", "Home", "Index.cshtml"));

        Assert.Contains("const initializeCharts = () => {", view);
        Assert.Contains("if (typeof window.Chart !== \"function\") return;", view);
        Assert.Contains("initializeCharts();", view);
        Assert.Contains("let productionChart;", view);
        Assert.Contains("if (!productionChart || !inventoryZoneChart || !qualityChart) return;", view);
        Assert.True(view.IndexOf("initializeCharts();", StringComparison.Ordinal) < view.IndexOf("const createConnection", StringComparison.Ordinal));
    }

    [Fact]
    public void DashboardView_RetriesHubsIndependentlyAndRejectsStaleMetricResponses()
    {
        var view = File.ReadAllText(Path.Combine(ProjectRoot(), "Views", "Home", "Index.cshtml"));

        Assert.Contains("startWithRetry", view);
        Assert.Contains("retryAttempt", view);
        Assert.DoesNotContain("Promise.all(connections", view);
        Assert.Contains("AbortController", view);
        Assert.Contains("refreshGeneration", view);
        Assert.Contains("connectivity", view);
        Assert.Contains("refreshState", view);
    }

    [Fact]
    public void RealtimeHubs_RequireAuthenticatedConnections()
    {
        Assert.NotNull(typeof(InventoryHub).GetCustomAttributes(typeof(AuthorizeAttribute), true).SingleOrDefault());
        Assert.NotNull(typeof(ProductionHub).GetCustomAttributes(typeof(AuthorizeAttribute), true).SingleOrDefault());
        var program = File.ReadAllText(Path.Combine(ProjectRoot(), "Program.cs"));
        Assert.Contains("MapHub<InventoryHub>(\"/inventoryHub\")", program);
        Assert.Contains("MapHub<ProductionHub>(\"/productionHub\")", program);
    }

    [Fact]
    public void RealtimeHubs_DoNotExposeClientCallableBroadcastMethods()
    {
        Assert.Empty(typeof(InventoryHub).GetMethods().Where(method => method.DeclaringType == typeof(InventoryHub)));
        Assert.Empty(typeof(ProductionHub).GetMethods().Where(method => method.DeclaringType == typeof(ProductionHub)));
    }

    [Fact]
    public void Metrics_DisablesResponseCaching()
    {
        var attribute = typeof(HomeController).GetMethod(nameof(HomeController.Metrics))!
            .GetCustomAttributes(typeof(ResponseCacheAttribute), true)
            .Cast<ResponseCacheAttribute>()
            .Single();

        Assert.True(attribute.NoStore);
        Assert.Equal(ResponseCacheLocation.None, attribute.Location);
    }

    private static ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Dashboard_{Guid.NewGuid()}").Options);

    private static HomeController Controller(ApplicationDbContext context) =>
        new(
            NullLogger<HomeController>.Instance,
            context,
            TimeProvider.System,
            TimeZoneInfo.CreateCustomTimeZone("Asia/Ho_Chi_Minh", TimeSpan.FromHours(7), "Vietnam", "Vietnam"));

    private static void SeedDashboard(ApplicationDbContext context)
    {
        var product = new Product { Id = 1, Code = "P", Name = "Product" };
        var normal = new Location { Id = 1, Code = "A-01", Name = "A", Zone = new Zone { Id = 1, Code = "A", Name = "A" } };
        var second = new Location { Id = 2, Code = "A-02", Name = "B", ZoneId = 1 };
        var quarantine = new Location { Id = 3, Code = "QC-QUARANTINE", Name = "QC", Zone = new Zone { Id = 2, Code = "QC", Name = "QC" } };
        var lot1 = new Lot { Id = 1, LotNo = "L1", Product = product };
        var lot2 = new Lot { Id = 2, LotNo = "L2", ProductId = 1 };
        var rejected = new Lot { Id = 3, LotNo = "L3", ProductId = 1 };
        context.WorkOrders.AddRange(
            NewWorkOrder("WO-1", WorkOrderStatus.InProgress, product),
            NewWorkOrder("WO-2", WorkOrderStatus.InProgress, product),
            NewWorkOrder("WO-3", WorkOrderStatus.Approved, product));
        context.StockBalances.AddRange(
            new StockBalance { Product = product, Lot = lot1, Location = normal, QtyAvailable = 10, QtyReserved = 2, QtyOnHold = 3 },
            new StockBalance { ProductId = 1, Lot = lot1, Location = second, QtyOnHold = 4 },
            new StockBalance { ProductId = 1, Lot = lot2, Location = normal, QtyAvailable = 5, QtyOnHold = 6 },
            new StockBalance { ProductId = 1, Lot = rejected, Location = quarantine, QtyAvailable = 8 });
    }

    private static WorkOrder NewWorkOrder(string code, WorkOrderStatus status, Product product) => new()
    {
        Code = code, Product = product, Qty = 1, DueDate = DateTime.UtcNow, Status = status,
        BomVersion = "1", RoutingVersion = "1"
    };

    private static string ProjectRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
