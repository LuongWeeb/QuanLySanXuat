using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Services;

namespace WmsMes.Web.Controllers;

[Authorize(Roles = "Admin,Manager,Planner")]
public class PurchaseOrderController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IPurchaseRequestService _requestService;
    private readonly IPurchaseOrderService _orderService;

    public PurchaseOrderController(
        ApplicationDbContext context,
        IPurchaseRequestService requestService,
        IPurchaseOrderService orderService)
    {
        _context = context;
        _requestService = requestService;
        _orderService = orderService;
    }

    public async Task<IActionResult> Index()
    {
        return View(await _orderService.GetAllAsync());
    }

    public async Task<IActionResult> Requests()
    {
        return View(await _requestService.GetAllAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var order = await _orderService.GetByIdAsync(id);
        return order is null ? NotFound() : View(order);
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? requestId)
    {
        await LoadSuppliersAsync();
        ViewData["Request"] = requestId.HasValue
            ? await _requestService.GetByIdAsync(requestId.Value)
            : null;
        ViewData["Requests"] = await _requestService.GetAllAsync();
        return View();
    }

    [HttpGet]
    public IActionResult CreateFromRequest(int requestId)
    {
        return RedirectToAction(nameof(Create), new { requestId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ActionName(nameof(CreateFromRequest))]
    public async Task<IActionResult> CreateFromRequestPost(
        int requestId,
        int supplierId)
    {
        var order = await _orderService.CreateOrderFromRequestAsync(
            requestId,
            supplierId,
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system");
        if (order is null)
        {
            TempData["ErrorMessage"] =
                "Không thể tạo Đơn mua hàng từ Yêu cầu mua hàng đã chọn.";
            return RedirectToAction(nameof(Create), new { requestId });
        }

        TempData["StatusMessage"] = $"Đã tạo Đơn mua hàng {order.OrderNo}.";
        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    private async Task LoadSuppliersAsync()
    {
        ViewData["Suppliers"] = await _context.Suppliers
            .AsNoTracking()
            .Where(supplier => supplier.IsActive)
            .OrderBy(supplier => supplier.Code)
            .ToListAsync();
    }
}
