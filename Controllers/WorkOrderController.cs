using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;
using WmsMes.Web.ViewModels;

namespace WmsMes.Web.Controllers;

[Authorize(Roles = "Admin,Planner,Manager")]
public class WorkOrderController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IWorkOrderService _workOrderService;
    private readonly ILogger<WorkOrderController> _logger;

    public WorkOrderController(ApplicationDbContext context, IWorkOrderService workOrderService, ILogger<WorkOrderController> logger)
    {
        _context = context;
        _workOrderService = workOrderService;
        _logger = logger;
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
    public async Task<IActionResult> Create(WorkOrderCreateInputModel input)
    {
        if (string.IsNullOrWhiteSpace(input.Code))
            ModelState.AddModelError(nameof(input.Code), "Mã lệnh là bắt buộc.");
        if (input.Qty <= 0)
            ModelState.AddModelError(nameof(input.Qty), "Số lượng phải lớn hơn 0.");
        if (input.DueDate is null)
            ModelState.AddModelError(nameof(input.DueDate), "Hạn hoàn thành là bắt buộc.");
        if (!await _context.Products.AnyAsync(x => x.Id == input.ProductId && x.IsManufactured && x.IsActive))
            ModelState.AddModelError(nameof(input.ProductId), "Thành phẩm không hợp lệ hoặc đã ngừng hoạt động.");

        if (!ModelState.IsValid)
        {
            await LoadProductsAsync();
            return View(input);
        }

        var workOrder = new WorkOrder
        {
            Code = input.Code.Trim(),
            ProductId = input.ProductId,
            Qty = input.Qty,
            DueDate = input.DueDate!.Value,
            Status = WorkOrderStatus.Draft,
            BomVersion = string.Empty,
            RoutingVersion = string.Empty
        };
        _context.WorkOrders.Add(workOrder);
        await _context.SaveChangesAsync();
        TempData["StatusMessage"] = $"Đã tạo lệnh sản xuất nháp {workOrder.Code}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin,Manager")]
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
            _logger.LogError(ex, "Failed to approve work order {WorkOrderId}.", id);
            TempData["StatusMessage"] = "Không thể phê duyệt lệnh sản xuất. Vui lòng thử lại hoặc liên hệ quản trị viên.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin,Manager")]
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
            _logger.LogError(ex, "Failed to complete work order {WorkOrderId}.", id);
            TempData["StatusMessage"] = "Không thể hoàn thành lệnh sản xuất. Vui lòng thử lại hoặc liên hệ quản trị viên.";
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
