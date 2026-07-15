using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WmsMes.Web.Services;

namespace WmsMes.Web.Controllers;

[Authorize(Roles = "Admin,Manager,Planner")]
public class MrpController : Controller
{
    private readonly IMrpService _mrpService;

    public MrpController(IMrpService mrpService)
    {
        _mrpService = mrpService;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Calculate(int productId, decimal qty)
    {
        var results = await _mrpService.CalculateRequirementsAsync(productId, qty);
        ViewData["ProductId"] = productId;
        ViewData["Qty"] = qty;
        return View("Index", results);
    }
}
