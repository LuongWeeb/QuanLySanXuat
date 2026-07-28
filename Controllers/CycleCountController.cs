using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Services;

namespace WmsMes.Web.Controllers;

[Authorize(Roles = "Admin,Warehouse,Manager")]
public class CycleCountController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ICycleCountService _countService;

    public CycleCountController(
        ApplicationDbContext context,
        ICycleCountService countService)
    {
        _context = context;
        _countService = countService;
    }

    public async Task<IActionResult> Index()
    {
        var orders = await _context.CycleCountOrders
            .AsNoTracking()
            .Include(order => order.Warehouse)
            .OrderByDescending(order => order.CreatedAt)
            .ToListAsync();
        return View(orders);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await LoadWarehousesAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int warehouseId)
    {
        if (!await _context.Warehouses.AnyAsync(warehouse =>
                warehouse.Id == warehouseId && warehouse.IsActive))
        {
            ModelState.AddModelError(
                nameof(warehouseId),
                "Kho không hợp lệ hoặc đã ngừng hoạt động.");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            ModelState.AddModelError(
                string.Empty,
                "Không thể xác định người tạo đợt kiểm kê.");
        }

        if (!ModelState.IsValid)
        {
            await LoadWarehousesAsync(warehouseId);
            return View();
        }

        var order = await _countService.CreateOrderAsync(warehouseId, userId!);
        return RedirectToAction(nameof(ExecuteScan), new { id = order.Id });
    }

    [HttpGet]
    public async Task<IActionResult> ExecuteScan(int id)
    {
        var order = await _countService.GetByIdAsync(id);
        return order is null ? NotFound() : View(order);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveScan(
        int id,
        Dictionary<int, decimal> itemCounts)
    {
        var saved = await _countService.UpdateCountedQtysAsync(
            id,
            itemCounts ?? []);
        if (!saved)
        {
            TempData["ErrorMessage"] =
                "Không thể lưu kết quả đếm. Vui lòng kiểm tra dữ liệu và thử lại.";
            return RedirectToAction(nameof(ExecuteScan), new { id });
        }

        TempData["StatusMessage"] = "Đã lưu kết quả kiểm đếm.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDiscoveredItem(
        int id,
        string locationCode,
        string lotNo,
        decimal countedQty)
    {
        var added = await _countService.AddDiscoveredItemAsync(
            id,
            locationCode,
            lotNo,
            countedQty);
        TempData[added ? "StatusMessage" : "ErrorMessage"] = added
            ? "Đã thêm lô phát hiện vào đợt kiểm kê."
            : "Không thể thêm lô. Kiểm tra vị trí, số lô và trạng thái đợt kiểm kê.";
        return RedirectToAction(nameof(ExecuteScan), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var order = await _countService.GetByIdAsync(id);
        return order is null ? NotFound() : View(order);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Approve(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            TempData["ErrorMessage"] =
                "Không thể xác định người duyệt đợt kiểm kê.";
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            var success = await _countService.ApproveAndAdjustLedgerAsync(
                id,
                userId);
            TempData[success ? "StatusMessage" : "ErrorMessage"] = success
                ? "Đã duyệt đợt kiểm kê và cập nhật sổ cái kho."
                : "Đợt kiểm kê không ở trạng thái sẵn sàng để duyệt.";
        }
        catch (InvalidOperationException)
        {
            TempData["ErrorMessage"] =
                "Không thể điều chỉnh tồn kho. Vui lòng kiểm tra chênh lệch và thử lại.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task LoadWarehousesAsync(int? selectedId = null)
    {
        ViewBag.Warehouses = await _context.Warehouses
            .AsNoTracking()
            .Where(warehouse => warehouse.IsActive)
            .OrderBy(warehouse => warehouse.Code)
            .ToListAsync();
        ViewBag.SelectedWarehouseId = selectedId;
    }
}
