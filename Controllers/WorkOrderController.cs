using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;

namespace WmsMes.Web.Controllers;

[Authorize(Roles = "Admin,Planner,Manager")]
public class WorkOrderController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IWorkOrderService _workOrderService;

    public WorkOrderController(ApplicationDbContext context, IWorkOrderService workOrderService)
    {
        _context = context;
        _workOrderService = workOrderService;
    }

    public async Task<IActionResult> Index()
    {
        var orders = await _context.WorkOrders.AsNoTracking()
            .Include(x => x.Product)
            .OrderByDescending(x => x.DueDate)
            .ToListAsync();
        return View(orders);
    }

    public async Task<IActionResult> Details(int id)
    {
        var order = await _context.WorkOrders.AsNoTracking()
            .Include(x => x.Product)
            .Include(x => x.Steps).ThenInclude(x => x.WorkCenter)
            .SingleOrDefaultAsync(x => x.Id == id);
        if (order is null) return NotFound();

        ViewData["Reservations"] = await _context.MaterialReservations.AsNoTracking()
            .Include(x => x.Product)
            .Include(x => x.Lot)
            .Include(x => x.Location)
            .Where(x => x.WorkOrderId == id)
            .ToListAsync();
        return View(order);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await LoadProductsAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(WorkOrder workOrder)
    {
        if (string.IsNullOrWhiteSpace(workOrder.Code))
            ModelState.AddModelError(nameof(workOrder.Code), "Mã lệnh là bắt buộc.");
        if (workOrder.Qty <= 0)
            ModelState.AddModelError(nameof(workOrder.Qty), "Số lượng phải lớn hơn 0.");
        if (workOrder.DueDate == default)
            ModelState.AddModelError(nameof(workOrder.DueDate), "Hạn hoàn thành là bắt buộc.");
        if (!await _context.Products.AnyAsync(x => x.Id == workOrder.ProductId && x.IsManufactured && x.IsActive))
            ModelState.AddModelError(nameof(workOrder.ProductId), "Thành phẩm không hợp lệ hoặc đã ngừng hoạt động.");

        if (!ModelState.IsValid)
        {
            await LoadProductsAsync();
            return View(workOrder);
        }

        workOrder.Code = workOrder.Code.Trim();
        workOrder.Status = WorkOrderStatus.Draft;
        _context.WorkOrders.Add(workOrder);
        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = $"Đã tạo lệnh sản xuất nháp {workOrder.Code}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id)
    {
        try
        {
            var success = await _workOrderService.ApproveWorkOrderAsync(id, CurrentUserId());
            TempData["StatusMessage"] = success
                ? "Đã phê duyệt lệnh sản xuất và giữ chỗ vật tư thành công."
                : "Không thể phê duyệt lệnh sản xuất này.";
        }
        catch (Exception ex)
        {
            TempData["StatusMessage"] = $"Lỗi khi duyệt lệnh: {ex.Message}";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(int id)
    {
        try
        {
            var success = await _workOrderService.CompleteWorkOrderAsync(id, CurrentUserId());
            TempData["StatusMessage"] = success
                ? "Đã hoàn thành lệnh sản xuất, trừ tồn vật tư và sinh lô thành phẩm chờ QC."
                : "Không thể hoàn thành lệnh sản xuất này. Đảm bảo tất cả các công đoạn đã hoàn thành.";
        }
        catch (Exception ex)
        {
            TempData["StatusMessage"] = $"Lỗi khi hoàn thành lệnh: {ex.Message}";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    private string CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";

    private async Task LoadProductsAsync() =>
        ViewData["Products"] = await _context.Products.AsNoTracking()
            .Where(x => x.IsManufactured && x.IsActive)
            .OrderBy(x => x.Code)
            .ToListAsync();
}
