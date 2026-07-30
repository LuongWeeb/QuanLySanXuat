using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WmsMes.Web.Services;

namespace WmsMes.Web.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly IOeeService _oeeService;

    public DashboardController(IOeeService oeeService)
    {
        _oeeService = oeeService;
    }

    public IActionResult Index()
    {
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetOeeData()
    {
        var endDate = DateTime.UtcNow;
        var startDate = endDate.AddDays(-6);
        var data = await _oeeService.GetAllWorkCentersOeeAsync(startDate, endDate);
        return Json(data);
    }

    [HttpGet]
    public async Task<IActionResult> GetAgingData()
    {
        var data = await _oeeService.GetInventoryAgingAnalyticsAsync();
        return Json(data);
    }

    [HttpGet]
    public async Task<IActionResult> GetProductionProgressData()
    {
        var data = await _oeeService.GetProductionProgressAnalyticsAsync();
        return Json(data);
    }
}
