using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
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
    public async Task<IActionResult> Search(string? q)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return RedirectToAction(nameof(Index));
        }

        var query = q.Trim();
        var canViewWorkOrders = User.IsInRole("Admin") || User.IsInRole("Planner") || User.IsInRole("Manager");
        var model = new SearchResultViewModel
        {
            Query = query,
            Products = await _context.Products
                .AsNoTracking()
                .Where(product => product.Code.Contains(query) || product.Name.Contains(query))
                .Take(10)
                .ToListAsync(),
            WorkOrders = canViewWorkOrders
                ? await _context.WorkOrders
                    .AsNoTracking()
                    .Where(workOrder => workOrder.Code.Contains(query))
                    .Take(10)
                    .ToListAsync()
                : new List<WorkOrder>(),
            Lots = await _context.Lots
                .AsNoTracking()
                .Include(lot => lot.Product)
                .Where(lot => lot.LotNo.Contains(query))
                .Take(10)
                .ToListAsync(),
            Locations = await _context.Locations
                .AsNoTracking()
                .Where(location => location.Code.Contains(query))
                .Take(10)
                .ToListAsync()
        };

        return View(model);
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
        var today = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), _businessTimeZone).Date;
        var startDate = today.AddDays(-6);
        var endDateExclusive = today.AddDays(1);
        var utcStart = ToUtc(startDate);
        var utcEndExclusive = ToUtc(endDateExclusive);
        var useSqliteCompatibility = _context.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        var workOrderRows = _context.WorkOrders
            .AsNoTracking()
            .Select(workOrder => new
            {
                workOrder.Status,
                TargetQuantity = workOrder.Qty
            });

        int totalWorkOrderCount;
        int activeWorkOrders;
        int completedOrInProgressCount;
        decimal totalTargetQuantity;
        if (useSqliteCompatibility)
        {
            var summary = await workOrderRows
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    TotalCount = group.Count(),
                    ActiveCount = group.Count(order => order.Status == WorkOrderStatus.InProgress),
                    CompletedOrInProgressCount = group.Count(order =>
                        order.Status == WorkOrderStatus.Completed || order.Status == WorkOrderStatus.InProgress),
                    TotalTargetQuantity = group.Sum(order => (double)order.TargetQuantity)
                })
                .SingleOrDefaultAsync();
            totalWorkOrderCount = summary?.TotalCount ?? 0;
            activeWorkOrders = summary?.ActiveCount ?? 0;
            completedOrInProgressCount = summary?.CompletedOrInProgressCount ?? 0;
            totalTargetQuantity = (decimal)(summary?.TotalTargetQuantity ?? 0d);
        }
        else
        {
            var summary = await workOrderRows
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    TotalCount = group.Count(),
                    ActiveCount = group.Count(order => order.Status == WorkOrderStatus.InProgress),
                    CompletedOrInProgressCount = group.Count(order =>
                        order.Status == WorkOrderStatus.Completed || order.Status == WorkOrderStatus.InProgress),
                    TotalTargetQuantity = group.Sum(order => order.TargetQuantity)
                })
                .SingleOrDefaultAsync();
            totalWorkOrderCount = summary?.TotalCount ?? 0;
            activeWorkOrders = summary?.ActiveCount ?? 0;
            completedOrInProgressCount = summary?.CompletedOrInProgressCount ?? 0;
            totalTargetQuantity = summary?.TotalTargetQuantity ?? 0m;
        }

        var finalSteps = _context.WorkOrderSteps
            .AsNoTracking()
            .Where(step => !_context.WorkOrderSteps.Any(candidate =>
                candidate.WorkOrderId == step.WorkOrderId
                && (candidate.StepNumber > step.StepNumber
                    || (candidate.StepNumber == step.StepNumber && candidate.Id > step.Id))));
        decimal totalAcceptedQuantity;
        decimal totalRejectedQuantity;
        if (useSqliteCompatibility)
        {
            var summary = await finalSteps
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    AcceptedQuantity = group.Sum(step => (double)step.QtyOK),
                    RejectedQuantity = group.Sum(step => (double)step.QtyReject)
                })
                .SingleOrDefaultAsync();
            totalAcceptedQuantity = (decimal)(summary?.AcceptedQuantity ?? 0d);
            totalRejectedQuantity = (decimal)(summary?.RejectedQuantity ?? 0d);
        }
        else
        {
            var summary = await finalSteps
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    AcceptedQuantity = group.Sum(step => step.QtyOK),
                    RejectedQuantity = group.Sum(step => step.QtyReject)
                })
                .SingleOrDefaultAsync();
            totalAcceptedQuantity = summary?.AcceptedQuantity ?? 0m;
            totalRejectedQuantity = summary?.RejectedQuantity ?? 0m;
        }
        var totalProducedQuantity = totalAcceptedQuantity + totalRejectedQuantity;

        var plannedRows = await _context.WorkOrders
            .AsNoTracking()
            .Where(workOrder => workOrder.DueDate >= startDate && workOrder.DueDate < endDateExclusive)
            .Select(workOrder => new { workOrder.DueDate, TargetQuantity = workOrder.Qty })
            .ToListAsync();
        var actualRows = await finalSteps
            .Where(step => step.EndTime >= utcStart && step.EndTime < utcEndExclusive)
            .Select(step => new { FinalEndTimeUtc = step.EndTime, FinalAcceptedQuantity = step.QtyOK })
            .ToListAsync();

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
        decimal inventoryVolume;
        int lowStockAlertCount;
        if (useSqliteCompatibility)
        {
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
            inventoryVolume = (decimal)(stockSummary?.InventoryVolume ?? 0d);
            lowStockAlertCount = stockSummary?.LowStockAlertCount ?? 0;
        }
        else
        {
            var stockSummary = await _context.StockBalances
                .AsNoTracking()
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    InventoryVolume = group.Sum(balance =>
                        balance.QtyAvailable + balance.QtyReserved + balance.QtyOnHold),
                    LowStockAlertCount = group.Count(balance => balance.QtyAvailable <= 10m)
                })
                .SingleOrDefaultAsync();
            inventoryVolume = stockSummary?.InventoryVolume ?? 0m;
            lowStockAlertCount = stockSummary?.LowStockAlertCount ?? 0;
        }

        var oeeAvailabilityPercent = totalWorkOrderCount == 0
            ? 0m
            : Math.Round(completedOrInProgressCount * 100m / totalWorkOrderCount, 1);
        var oeePerformancePercent = totalTargetQuantity == 0m
            ? 0m
            : Math.Round(totalProducedQuantity * 100m / totalTargetQuantity, 1);
        var oeeQualityPercent = totalProducedQuantity == 0m
            ? 0m
            : Math.Round(totalAcceptedQuantity * 100m / totalProducedQuantity, 1);

        var dailyLabels = new List<string>();
        var dailyPlannedOutput = new List<decimal>();
        var dailyActualOutput = new List<decimal>();
        for (var date = startDate; date <= today; date = date.AddDays(1))
        {
            dailyLabels.Add(date.ToString("dd/MM"));
            dailyPlannedOutput.Add(plannedRows
                .Where(workOrder => workOrder.DueDate.Date == date)
                .Sum(workOrder => workOrder.TargetQuantity));
            dailyActualOutput.Add(actualRows
                .Where(row => row.FinalEndTimeUtc is DateTime endTimeUtc && ToBusinessDate(endTimeUtc) == date)
                .Sum(row => row.FinalAcceptedQuantity));
        }

        List<string> zoneLabels;
        List<decimal> zoneQuantities;
        if (useSqliteCompatibility)
        {
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
            zoneLabels = zoneInventory.Select(zone => zone.Name).ToList();
            zoneQuantities = zoneInventory.Select(zone => (decimal)zone.Quantity).ToList();
        }
        else
        {
            var zoneInventory = await _context.StockBalances
                .AsNoTracking()
                .Where(balance => balance.Location!.Zone != null)
                .GroupBy(balance => balance.Location!.Zone!.Name)
                .OrderBy(group => group.Key)
                .Select(group => new
                {
                    Name = group.Key,
                    Quantity = group.Sum(balance =>
                        balance.QtyAvailable + balance.QtyReserved + balance.QtyOnHold)
                })
                .ToListAsync();
            zoneLabels = zoneInventory.Select(zone => zone.Name).ToList();
            zoneQuantities = zoneInventory.Select(zone => zone.Quantity).ToList();
        }

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
            ZoneLabels = zoneLabels,
            ZoneQuantities = zoneQuantities,
            PassedQcCount = passedQcCount,
            HoldQcCount = holdQcCount,
            QuarantineQcCount = quarantineQcCount
        };
    }

    private DateTime ToBusinessDate(DateTime utcDateTime) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc), _businessTimeZone).Date;

    private DateTime ToUtc(DateTime businessDate) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(businessDate, DateTimeKind.Unspecified), _businessTimeZone);
}
