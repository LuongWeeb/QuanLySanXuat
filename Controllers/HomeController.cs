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
        var activeWorkOrders = await _context.WorkOrders
            .AsNoTracking()
            .CountAsync(workOrder => workOrder.Status == WorkOrderStatus.InProgress);
        var pendingQcLots = await _context.StockBalances
            .AsNoTracking()
            .Where(balance => balance.QtyOnHold > 0 && balance.Location!.Code != QcService.QuarantineLocationCode)
            .Select(balance => balance.LotId)
            .Distinct()
            .CountAsync();
        var inventoryVolume = await _context.StockBalances
            .AsNoTracking()
            .SumAsync(balance => balance.QtyAvailable + balance.QtyReserved + balance.QtyOnHold);

        return new DashboardViewModel
        {
            ActiveWorkOrders = activeWorkOrders,
            PendingQcLots = pendingQcLots,
            InventoryVolume = inventoryVolume
        };
    }
}
