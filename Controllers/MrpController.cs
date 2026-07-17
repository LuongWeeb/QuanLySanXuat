using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Services;

namespace WmsMes.Web.Controllers;

[Authorize(Roles = "Admin,Manager,Planner")]
public class MrpController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IMrpService _mrpService;

    public MrpController(ApplicationDbContext context, IMrpService mrpService)
    {
        _context = context;
        _mrpService = mrpService;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        await LoadProductsAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Calculate(int productId, decimal qty)
    {
        ViewData["ProductId"] = productId;
        ViewData["Qty"] = qty;
        await LoadProductsAsync();

        if (qty <= 0)
            ModelState.AddModelError(nameof(qty), "Số lượng kế hoạch phải lớn hơn 0.");

        var productIsValid = await _context.Products
            .AnyAsync(p => p.Id == productId && p.IsManufactured && p.IsActive);
        if (!productIsValid)
            ModelState.AddModelError(nameof(productId), "Vui lòng chọn một sản phẩm sản xuất đang hoạt động.");

        if (!ModelState.IsValid)
            return View("Index");

        try
        {
            var results = await _mrpService.CalculateRequirementsAsync(productId, qty);
            return View("Index", results);
        }
        catch (InvalidOperationException)
        {
            ModelState.AddModelError(string.Empty, "Không thể tính MRP cho sản phẩm đã chọn. Vui lòng kiểm tra BOM và thử lại.");
            return View("Index");
        }
    }

    private async Task LoadProductsAsync() =>
        ViewData["Products"] = await _context.Products
            .Where(p => p.IsManufactured && p.IsActive)
            .OrderBy(p => p.Code)
            .ToListAsync();
}
