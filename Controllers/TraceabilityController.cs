using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WmsMes.Web.Services;

namespace WmsMes.Web.Controllers;

[Authorize]
public class TraceabilityController : Controller
{
    private readonly ITraceabilityService _traceabilityService;

    public TraceabilityController(ITraceabilityService traceabilityService)
    {
        _traceabilityService = traceabilityService;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetTree(string lotNo, string direction = "backward")
    {
        if (string.IsNullOrWhiteSpace(lotNo))
        {
            return Json(null);
        }

        var tree = direction.Equals("forward", StringComparison.OrdinalIgnoreCase)
            ? await _traceabilityService.GetForwardTraceAsync(lotNo.Trim())
            : await _traceabilityService.GetBackwardTraceAsync(lotNo.Trim());

        return Json(tree);
    }
}
