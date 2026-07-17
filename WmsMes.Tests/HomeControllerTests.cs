using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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

        var result = await new HomeController(NullLogger<HomeController>.Instance, context).Index();

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

        var result = await new HomeController(NullLogger<HomeController>.Instance, context).Metrics();

        var metrics = Assert.IsType<DashboardViewModel>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal((2, 2, 38m), (metrics.ActiveWorkOrders, metrics.PendingQcLots, metrics.InventoryVolume));
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
