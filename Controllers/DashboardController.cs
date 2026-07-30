using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WmsMes.Web.Services;
using WmsMes.Web.ViewModels;

namespace WmsMes.Web.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly IOeeService _oeeService;
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _businessTimeZone;
    private readonly ILowStockService _lowStockService;

    public DashboardController(
        IOeeService oeeService,
        TimeProvider timeProvider,
        TimeZoneInfo businessTimeZone,
        ILowStockService lowStockService)
    {
        _oeeService = oeeService;
        _timeProvider = timeProvider;
        _businessTimeZone = businessTimeZone;
        _lowStockService = lowStockService;
    }

    public async Task<IActionResult> Index()
    {
        return View(new DashboardViewModel
        {
            LowStockItems = await _lowStockService.GetLowStockItemsAsync()
        });
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetOeeData()
    {
        var period = GetCurrentReportingPeriod();
        var data = await _oeeService.GetAllWorkCentersOeeAsync(
            period.Start,
            period.EndExclusive);
        return Json(data);
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetAgingData()
    {
        var data = await _oeeService.GetInventoryAgingAnalyticsAsync();
        return Json(data);
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetProductionProgressData()
    {
        var data = await _oeeService.GetProductionProgressAnalyticsAsync();
        return Json(data);
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetProductionQualityData()
    {
        var period = GetCurrentReportingPeriod();
        var data = await _oeeService.GetProductionQualityAnalyticsAsync(
            period.Start,
            period.EndExclusive);
        return Json(data);
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetLowStockAlert()
    {
        return PartialView(
            "_LowStockAlert",
            await _lowStockService.GetLowStockItemsAsync(
                HttpContext.RequestAborted));
    }

    private ReportingPeriod GetCurrentReportingPeriod()
    {
        var businessToday =
            TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), _businessTimeZone).Date;
        var firstBusinessDate = businessToday.AddDays(-6);
        var endBusinessDateExclusive = businessToday.AddDays(1);
        return new ReportingPeriod(
            TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(firstBusinessDate, DateTimeKind.Unspecified),
                _businessTimeZone),
            TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(endBusinessDateExclusive, DateTimeKind.Unspecified),
                _businessTimeZone));
    }

    private readonly record struct ReportingPeriod(
        DateTime Start,
        DateTime EndExclusive);
}
