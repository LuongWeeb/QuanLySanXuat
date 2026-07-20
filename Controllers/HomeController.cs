using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Models;
using WmsMes.Web.Services;
using WmsMes.Web.ViewModels;

namespace WmsMes.Web.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _businessTimeZone;

    public HomeController(
        ILogger<HomeController> logger,
        ApplicationDbContext context,
        TimeProvider timeProvider,
        TimeZoneInfo businessTimeZone)
    {
        _logger = logger;
        _context = context;
        _timeProvider = timeProvider;
        _businessTimeZone = businessTimeZone;
    }

    public async Task<IActionResult> Index()
    {
        return View(await GetMetricsAsync());
    }

    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Metrics()
    {
        return Ok(await GetMetricsAsync());
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    private async Task<DashboardViewModel> GetMetricsAsync()
    {
        var workOrders = await _context.WorkOrders
            .AsNoTracking()
            .Select(workOrder => new
            {
                workOrder.Status,
                TargetQuantity = workOrder.Qty,
                workOrder.DueDate,
                FinalEndTimeUtc = workOrder.Steps
                    .OrderByDescending(step => step.StepNumber)
                    .Select(step => step.EndTime)
                    .FirstOrDefault(),
                FinalAcceptedQuantity = workOrder.Steps
                    .OrderByDescending(step => step.StepNumber)
                    .Select(step => (decimal?)step.QtyOK)
                    .FirstOrDefault() ?? 0m,
                FinalRejectedQuantity = workOrder.Steps
                    .OrderByDescending(step => step.StepNumber)
                    .Select(step => (decimal?)step.QtyReject)
                    .FirstOrDefault() ?? 0m
            })
            .ToListAsync();

        var activeWorkOrders = workOrders.Count(workOrder => workOrder.Status == WorkOrderStatus.InProgress);
        var passedQcCount = await _context.QCInspections
            .AsNoTracking()
            .CountAsync(inspection => inspection.Result == QCResult.PASS);
        var holdQcCount = await _context.StockBalances
            .AsNoTracking()
            .Where(balance => balance.QtyOnHold > 0 && balance.Location!.Code != QcService.QuarantineLocationCode)
            .Select(balance => balance.LotId)
            .Distinct()
            .CountAsync();
        var quarantineQcCount = await _context.StockBalances
            .AsNoTracking()
            .Where(balance => balance.QtyOnHold > 0 && balance.Location!.Code == QcService.QuarantineLocationCode)
            .Select(balance => balance.LotId)
            .Distinct()
            .CountAsync();
        var stockSummary = await _context.StockBalances
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new
            {
                InventoryVolume = group.Sum(balance =>
                    (double)(balance.QtyAvailable + balance.QtyReserved + balance.QtyOnHold)),
                LowStockAlertCount = group.Count(balance => balance.QtyAvailable <= 10m)
            })
            .SingleOrDefaultAsync();
        var inventoryVolume = (decimal)(stockSummary?.InventoryVolume ?? 0d);
        var lowStockAlertCount = stockSummary?.LowStockAlertCount ?? 0;

        var completedOrInProgressCount = workOrders.Count(workOrder =>
            workOrder.Status is WorkOrderStatus.Completed or WorkOrderStatus.InProgress);
        var totalTargetQuantity = workOrders.Sum(workOrder => workOrder.TargetQuantity);
        var totalProducedQuantity = workOrders.Sum(workOrder => workOrder.FinalAcceptedQuantity + workOrder.FinalRejectedQuantity);
        var totalAcceptedQuantity = workOrders.Sum(workOrder => workOrder.FinalAcceptedQuantity);

        var oeeAvailabilityPercent = workOrders.Count == 0
            ? 0m
            : Math.Round(completedOrInProgressCount * 100m / workOrders.Count, 1);
        var oeePerformancePercent = totalTargetQuantity == 0m
            ? 0m
            : Math.Round(totalProducedQuantity * 100m / totalTargetQuantity, 1);
        var oeeQualityPercent = totalProducedQuantity == 0m
            ? 0m
            : Math.Round(totalAcceptedQuantity * 100m / totalProducedQuantity, 1);

        var today = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), _businessTimeZone).Date;
        var startDate = today.AddDays(-6);
        var dailyLabels = new List<string>();
        var dailyPlannedOutput = new List<decimal>();
        var dailyActualOutput = new List<decimal>();
        for (var date = startDate; date <= today; date = date.AddDays(1))
        {
            dailyLabels.Add(date.ToString("dd/MM"));
            dailyPlannedOutput.Add(workOrders
                .Where(workOrder => workOrder.DueDate.Date == date)
                .Sum(workOrder => workOrder.TargetQuantity));
            dailyActualOutput.Add(workOrders
                .Where(workOrder => workOrder.FinalEndTimeUtc is DateTime endTimeUtc && ToBusinessDate(endTimeUtc) == date)
                .Sum(workOrder => workOrder.FinalAcceptedQuantity));
        }

        var zoneInventory = await _context.StockBalances
            .AsNoTracking()
            .Where(balance => balance.Location!.Zone != null)
            .GroupBy(balance => balance.Location!.Zone!.Name)
            .OrderBy(group => group.Key)
            .Select(group => new
            {
                Name = group.Key,
                Quantity = group.Sum(balance =>
                    (double)(balance.QtyAvailable + balance.QtyReserved + balance.QtyOnHold))
            })
            .ToListAsync();

        return new DashboardViewModel
        {
            ActiveWorkOrders = activeWorkOrders,
            PendingQcLots = holdQcCount,
            InventoryVolume = inventoryVolume,
            LowStockAlertCount = lowStockAlertCount,
            OeeAvailabilityPercent = oeeAvailabilityPercent,
            OeePerformancePercent = oeePerformancePercent,
            OeeQualityPercent = oeeQualityPercent,
            DailyLabels = dailyLabels,
            DailyPlannedOutput = dailyPlannedOutput,
            DailyActualOutput = dailyActualOutput,
            ZoneLabels = zoneInventory.Select(zone => zone.Name).ToList(),
            ZoneQuantities = zoneInventory.Select(zone => (decimal)zone.Quantity).ToList(),
            PassedQcCount = passedQcCount,
            HoldQcCount = holdQcCount,
            QuarantineQcCount = quarantineQcCount
        };
    }

    private DateTime ToBusinessDate(DateTime utcDateTime) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc), _businessTimeZone).Date;
}
