using System.Security.Claims;
using System.Data;
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
    private readonly IReportExportService _reportExportService;
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _businessTimeZone;

    public WorkOrderController(
        ApplicationDbContext context,
        IWorkOrderService workOrderService,
        ILogger<WorkOrderController> logger,
        IReportExportService reportExportService,
        TimeProvider timeProvider,
        TimeZoneInfo businessTimeZone)
    {
        _context = context;
        _workOrderService = workOrderService;
        _logger = logger;
        _reportExportService = reportExportService ?? throw new ArgumentNullException(nameof(reportExportService));
        _timeProvider = timeProvider;
        _businessTimeZone = businessTimeZone;
    }

    public async Task<IActionResult> Index()
    {
        var orders = await _context.WorkOrders.AsNoTracking()
            .Include(x => x.Product)
            .Include(x => x.DailyProductionLogs)
            .OrderByDescending(x => x.DueDate)
            .ToListAsync();
        ViewData["BusinessDate"] = GetBusinessDate();
        return View(orders);
    }

    public async Task<IActionResult> Details(int id)
    {
        var order = await _context.WorkOrders.AsNoTracking()
            .Include(x => x.Product)
            .Include(x => x.Steps).ThenInclude(x => x.WorkCenter)
            .Include(x => x.DailyProductionLogs)
            .SingleOrDefaultAsync(x => x.Id == id);
        if (order is null) return NotFound();

        ViewData["BusinessDate"] = GetBusinessDate();
        ViewData["Reservations"] = await _context.MaterialReservations.AsNoTracking()
            .Include(x => x.Product)
            .Include(x => x.Lot)
            .Include(x => x.Location)
            .Where(x => x.WorkOrderId == id)
            .ToListAsync();
        return View(order);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDailyLog(int id, DailyProductionLogInputModel input)
    {
        ValidateDailyLogInput(input);
        if (!ModelState.IsValid)
            return await DetailsWithValidationAsync(id);

        if (!_context.Database.IsRelational())
        {
            try
            {
                return DailyLogResult(await SaveDailyLogAsync(id), id);
            }
            catch (Exception ex)
            {
                return DailyLogFailure(ex, id);
            }
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable);
        try
        {
            var result = await SaveDailyLogAsync(id);
            if (result == DailyLogSaveResult.Saved)
                await transaction.CommitAsync();
            else
                await transaction.RollbackAsync();

            return DailyLogResult(result, id);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return DailyLogFailure(ex, id);
        }

        async Task<DailyLogSaveResult> SaveDailyLogAsync(int workOrderId)
        {
            var order = await _context.WorkOrders
                .SingleOrDefaultAsync(x => x.Id == workOrderId);
            if (order is null)
                return DailyLogSaveResult.Missing;
            if (order.Status != WorkOrderStatus.InProgress)
                return DailyLogSaveResult.WrongStatus;

            _context.DailyProductionLogs.Add(new DailyProductionLog
            {
                WorkOrderId = order.Id,
                Date = input.Date!.Value.Date,
                QtyProduced = input.QtyProduced,
                Notes = input.Notes?.Trim() ?? string.Empty
            });
            await _context.SaveChangesAsync();
            return DailyLogSaveResult.Saved;
        }
    }

    [HttpGet("[controller]/[action]/{id:int}")]
    public async Task<IActionResult> ExportPdf(int id)
    {
        try
        {
            var bytes = await _reportExportService.ExportWorkOrderToPdfAsync(id);
            return File(bytes, "application/pdf", $"LenhSanXuat_{id}_{DateTime.Now:yyyyMMdd}.pdf");
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
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

    private DateTime GetBusinessDate() =>
        TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), _businessTimeZone).Date;

    private void ValidateDailyLogInput(DailyProductionLogInputModel input)
    {
        if (input.Date is null && !HasModelError(nameof(input.Date)))
            ModelState.AddModelError(nameof(input.Date), "Ngày sản xuất là bắt buộc.");
        if (input.QtyProduced <= 0 && !HasModelError(nameof(input.QtyProduced)))
            ModelState.AddModelError(nameof(input.QtyProduced), "Số lượng sản xuất phải lớn hơn 0.");
        if (input.Notes?.Length > 250 && !HasModelError(nameof(input.Notes)))
            ModelState.AddModelError(nameof(input.Notes), "Ghi chú không được vượt quá 250 ký tự.");

        bool HasModelError(string key) =>
            ModelState.TryGetValue(key, out var entry) && entry.Errors.Count > 0;
    }

    private async Task<IActionResult> DetailsWithValidationAsync(int id)
    {
        var result = await Details(id);
        if (result is ViewResult view)
            view.ViewName = nameof(Details);
        return result;
    }

    private IActionResult DailyLogResult(DailyLogSaveResult result, int id)
    {
        if (result == DailyLogSaveResult.Missing)
            return NotFound();

        TempData["StatusMessage"] = result == DailyLogSaveResult.Saved
            ? "Đã ghi nhận sản lượng sản xuất hàng ngày."
            : "Chỉ có thể ghi sản lượng khi lệnh đang sản xuất.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private IActionResult DailyLogFailure(Exception exception, int id)
    {
        _logger.LogError(exception, "Failed to add daily production log for work order {WorkOrderId}.", id);
        TempData["StatusMessage"] = "Không thể ghi nhận sản lượng sản xuất. Vui lòng thử lại.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task LoadProductsAsync() =>
        ViewData["Products"] = await _context.Products.AsNoTracking()
            .Where(x => x.IsManufactured && x.IsActive)
            .OrderBy(x => x.Code)
            .ToListAsync();

    private enum DailyLogSaveResult
    {
        Missing,
        WrongStatus,
        Saved
    }
}
