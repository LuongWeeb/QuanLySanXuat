using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;
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
        var quarantineZone = new Zone { Code = "QUAR", Name = "Quarantine" };
        var finishedLocation = new Location { Code = "FG-01", Name = "FG-01", Zone = finishedGoods };
        var finishedHoldingLocation = new Location { Code = "FG-02", Name = "FG-02", Zone = finishedGoods };
        var rawLocation = new Location { Code = "RM-01", Name = "RM-01", Zone = rawMaterials };
        var quarantineLocation = new Location { Code = QcService.QuarantineLocationCode, Name = "Quarantine", Zone = quarantineZone };
        var completedOrder = WorkOrder("WO-COMPLETE", 100m, today, WorkOrderStatus.Completed,
            new WorkOrderStep { StepNumber = 10, QtyOK = 90m, QtyReject = 10m, EndTime = today.AddHours(8), Status = WorkOrderStepStatus.Completed });
        var heldLot = Lot("HELD");
        var quarantinedLot = Lot("QUARANTINED");
        var completedBalance = Balance(10m, finishedLocation);
        var rawBalance = Balance(11m, rawLocation);

        context.WorkOrders.AddRange(
            completedOrder,
            WorkOrder("WO-IN-PROGRESS", 50m, today.AddDays(-1), WorkOrderStatus.InProgress),
            WorkOrder("WO-DRAFT", 50m, today.AddDays(-2), WorkOrderStatus.Draft));
        context.StockBalances.AddRange(
            completedBalance,
            Balance(4m, finishedLocation),
            rawBalance,
            Balance(11m, finishedLocation, heldLot, qtyOnHold: 1m),
            Balance(11m, finishedHoldingLocation, heldLot, qtyOnHold: 2m),
            Balance(11m, quarantineLocation, quarantinedLot, qtyOnHold: 1m));
        context.QCInspections.AddRange(
            new QCInspection { WorkOrder = completedOrder, Lot = completedBalance.Lot, InspectorId = "qc-1", Result = QCResult.PASS },
            new QCInspection { WorkOrder = completedOrder, Lot = rawBalance.Lot, InspectorId = "qc-1", Result = QCResult.REJECT });
        await context.SaveChangesAsync();

        var result = await Controller(context).Metrics();

        var metrics = Assert.IsType<DashboardViewModel>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(2, metrics.LowStockAlertCount);
        Assert.Equal(1, metrics.PassedQcCount);
        Assert.Equal(1, metrics.HoldQcCount);
        Assert.Equal(1, metrics.QuarantineQcCount);
        Assert.Equal(66.7m, metrics.OeeAvailabilityPercent);
        Assert.Equal(50m, metrics.OeePerformancePercent);
        Assert.Equal(90m, metrics.OeeQualityPercent);
        Assert.Equal(30m, metrics.OverallOeePercent);
        Assert.Equal(Enumerable.Range(0, 7).Select(day => today.AddDays(day - 6).ToString("dd/MM")), metrics.DailyLabels);
        Assert.Equal(new[] { 0m, 0m, 0m, 0m, 50m, 50m, 100m }, metrics.DailyPlannedOutput);
        Assert.Equal(new[] { 0m, 0m, 0m, 0m, 0m, 0m, 90m }, metrics.DailyActualOutput);
        Assert.Equal(new[] { "Finished Goods", "Quarantine", "Raw Materials" }, metrics.ZoneLabels);
        Assert.Equal(new[] { 39m, 12m, 11m }, metrics.ZoneQuantities);
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

    private static Lot Lot(string lotNo) =>
        new() { LotNo = lotNo, Product = new Product { Code = $"P-{lotNo}", Name = $"Product {lotNo}" } };

    private static StockBalance Balance(decimal qtyAvailable, Location location, Lot? lot = null, decimal qtyOnHold = 0m) =>
        new()
        {
            Product = new Product { Code = Guid.NewGuid().ToString(), Name = "Product" },
            Lot = lot ?? Lot(Guid.NewGuid().ToString()),
            Location = location,
            QtyAvailable = qtyAvailable,
            QtyOnHold = qtyOnHold
        };
}
