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

    public HomeController(ILogger<HomeController> logger, ApplicationDbContext context)
    {
        _logger = logger;
        _context = context;
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
            .Include(workOrder => workOrder.Steps)
            .AsNoTracking()
            .ToListAsync();
        var stockBalances = await _context.StockBalances
            .Include(balance => balance.Location)
            .ThenInclude(location => location!.Zone)
            .AsNoTracking()
            .ToListAsync();

        var activeWorkOrders = workOrders.Count(workOrder => workOrder.Status == WorkOrderStatus.InProgress);
        var pendingQcLots = await _context.StockBalances
            .AsNoTracking()
            .Where(balance => balance.QtyOnHold > 0 && balance.Location!.Code != QcService.QuarantineLocationCode)
            .Select(balance => balance.LotId)
            .Distinct()
            .CountAsync();
        var inventoryVolume = stockBalances.Sum(balance => balance.QtyAvailable + balance.QtyReserved + balance.QtyOnHold);
        var lowStockAlertCount = stockBalances.Count(balance => balance.QtyAvailable <= 10m);

        var oeeOrders = workOrders
            .Select(workOrder => new
            {
                FinalStep = workOrder.Steps.OrderByDescending(step => step.StepNumber).FirstOrDefault()
            })
            .ToList();
        var completedOrInProgressCount = workOrders.Count(workOrder =>
            workOrder.Status is WorkOrderStatus.Completed or WorkOrderStatus.InProgress);
        var totalTargetQuantity = workOrders.Sum(workOrder => workOrder.Qty);
        var totalProducedQuantity = oeeOrders.Sum(order => (order.FinalStep?.QtyOK ?? 0m) + (order.FinalStep?.QtyReject ?? 0m));
        var totalAcceptedQuantity = oeeOrders.Sum(order => order.FinalStep?.QtyOK ?? 0m);

        var oeeAvailabilityPercent = workOrders.Count == 0
            ? 0m
            : Math.Round(completedOrInProgressCount * 100m / workOrders.Count, 1);
        var oeePerformancePercent = totalTargetQuantity == 0m
            ? 0m
            : Math.Round(totalProducedQuantity * 100m / totalTargetQuantity, 1);
        var oeeQualityPercent = totalProducedQuantity == 0m
            ? 0m
            : Math.Round(totalAcceptedQuantity * 100m / totalProducedQuantity, 1);

        var today = DateTime.Today;
        var startDate = today.AddDays(-6);
        var dailyLabels = new List<string>();
        var dailyPlannedOutput = new List<decimal>();
        var dailyActualOutput = new List<decimal>();
        for (var date = startDate; date <= today; date = date.AddDays(1))
        {
            dailyLabels.Add(date.ToString("dd/MM"));
            dailyPlannedOutput.Add(workOrders.Where(workOrder => workOrder.DueDate.Date == date).Sum(workOrder => workOrder.Qty));
            dailyActualOutput.Add(oeeOrders
                .Where(order => order.FinalStep?.EndTime?.Date == date)
                .Sum(order => order.FinalStep?.QtyOK ?? 0m));
        }

        var zoneInventory = stockBalances
            .Where(balance => balance.Location?.Zone != null)
            .GroupBy(balance => balance.Location!.Zone!.Name)
            .OrderBy(group => group.Key)
            .Select(group => new
            {
                Name = group.Key,
                Quantity = group.Sum(balance => balance.QtyAvailable + balance.QtyReserved + balance.QtyOnHold)
            })
            .ToList();

        return new DashboardViewModel
        {
            ActiveWorkOrders = activeWorkOrders,
            PendingQcLots = pendingQcLots,
            InventoryVolume = inventoryVolume,
            LowStockAlertCount = lowStockAlertCount,
            OeeAvailabilityPercent = oeeAvailabilityPercent,
            OeePerformancePercent = oeePerformancePercent,
            OeeQualityPercent = oeeQualityPercent,
            DailyLabels = dailyLabels,
            DailyPlannedOutput = dailyPlannedOutput,
            DailyActualOutput = dailyActualOutput,
            ZoneLabels = zoneInventory.Select(zone => zone.Name).ToList(),
            ZoneQuantities = zoneInventory.Select(zone => zone.Quantity).ToList()
        };
    }
}
