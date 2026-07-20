using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.ViewModels;

namespace WmsMes.Tests;

public class DashboardMetricsTests
{
    [Fact]
    public async Task Metrics_CalculatesOeeAlertsDailyOutputAndZoneInventory()
    {
        await using var context = new ApplicationDbContext(Options($"Dashboard_{Guid.NewGuid()}"));
        var today = DateTime.Today;
        var finishedGoods = new Zone { Code = "FG", Name = "Finished Goods" };
        var rawMaterials = new Zone { Code = "RM", Name = "Raw Materials" };
        var finishedLocation = new Location { Code = "FG-01", Name = "FG-01", Zone = finishedGoods };
        var rawLocation = new Location { Code = "RM-01", Name = "RM-01", Zone = rawMaterials };

        context.WorkOrders.AddRange(
            WorkOrder("WO-COMPLETE", 100m, today, WorkOrderStatus.Completed,
                new WorkOrderStep { StepNumber = 10, QtyOK = 90m, QtyReject = 10m, EndTime = today.AddHours(8), Status = WorkOrderStepStatus.Completed }),
            WorkOrder("WO-IN-PROGRESS", 50m, today.AddDays(-1), WorkOrderStatus.InProgress),
            WorkOrder("WO-DRAFT", 50m, today.AddDays(-2), WorkOrderStatus.Draft));
        context.StockBalances.AddRange(
            Balance(10m, finishedLocation),
            Balance(4m, finishedLocation),
            Balance(11m, rawLocation));
        await context.SaveChangesAsync();

        var result = await Controller(context).Metrics();

        var metrics = Assert.IsType<DashboardViewModel>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(2, metrics.LowStockAlertCount);
        Assert.Equal(66.7m, metrics.OeeAvailabilityPercent);
        Assert.Equal(50m, metrics.OeePerformancePercent);
        Assert.Equal(90m, metrics.OeeQualityPercent);
        Assert.Equal(30m, metrics.OverallOeePercent);
        Assert.Equal(Enumerable.Range(0, 7).Select(day => today.AddDays(day - 6).ToString("dd/MM")), metrics.DailyLabels);
        Assert.Equal(new[] { 0m, 0m, 0m, 0m, 50m, 50m, 100m }, metrics.DailyPlannedOutput);
        Assert.Equal(new[] { 0m, 0m, 0m, 0m, 0m, 0m, 90m }, metrics.DailyActualOutput);
        Assert.Equal(new[] { "Finished Goods", "Raw Materials" }, metrics.ZoneLabels);
        Assert.Equal(new[] { 14m, 11m }, metrics.ZoneQuantities);
    }

    private static DbContextOptions<ApplicationDbContext> Options(string name) =>
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options;

    private static HomeController Controller(ApplicationDbContext context) =>
        new(NullLogger<HomeController>.Instance, context);

    private static WorkOrder WorkOrder(string code, decimal qty, DateTime dueDate, WorkOrderStatus status, params WorkOrderStep[] steps) =>
        new()
        {
            Code = code,
            Qty = qty,
            DueDate = dueDate,
            Status = status,
            BomVersion = "B1",
            RoutingVersion = "R1",
            Steps = steps
        };

    private static StockBalance Balance(decimal qtyAvailable, Location location) =>
        new()
        {
            Product = new Product { Code = Guid.NewGuid().ToString(), Name = "Product" },
            Lot = new Lot { LotNo = Guid.NewGuid().ToString(), Product = new Product { Code = Guid.NewGuid().ToString(), Name = "Lot product" } },
            Location = location,
            QtyAvailable = qtyAvailable
        };
}
