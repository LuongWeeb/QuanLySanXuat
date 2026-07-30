using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WmsMes.Web.Services;

namespace WmsMes.Web.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly IOeeService _oeeService;
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _businessTimeZone;

    public DashboardController(
        IOeeService oeeService,
        TimeProvider timeProvider,
        TimeZoneInfo businessTimeZone)
    {
        _oeeService = oeeService;
        _timeProvider = timeProvider;
        _businessTimeZone = businessTimeZone;
    }

    public IActionResult Index()
    {
        return View();
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
