using System.Security.Claims;
using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Hubs;
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
    private readonly IHubContext<ProductionHub>? _productionHub;

    public WorkOrderController(
        ApplicationDbContext context,
        IWorkOrderService workOrderService,
        ILogger<WorkOrderController> logger,
        IReportExportService reportExportService,
        TimeProvider timeProvider,
        TimeZoneInfo businessTimeZone,
        IHubContext<ProductionHub>? productionHub = null)
    {
        _context = context;
        _workOrderService = workOrderService;
        _logger = logger;
        _reportExportService = reportExportService ?? throw new ArgumentNullException(nameof(reportExportService));
        _timeProvider = timeProvider;
        _businessTimeZone = businessTimeZone;
        _productionHub = productionHub;
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
        var reservations = await _context.MaterialReservations.AsNoTracking()
            .Include(x => x.Product)
            .Include(x => x.Lot)
            .Include(x => x.Location)
            .Where(x => x.WorkOrderId == id)
            .ToListAsync();
        var targetSnapshotPresent =
            order.TargetMaterialCost.HasValue &&
            order.TargetLaborCost.HasValue &&
            order.TargetMachineCost.HasValue;
        var actualSnapshotPresent =
            order.ActualMaterialCost.HasValue &&
            order.ActualLaborCost.HasValue &&
            order.ActualMachineCost.HasValue;
        decimal? selectedBomMaterialCost = null;
        if (!targetSnapshotPresent)
        {
            var bomQuery = _context.BOMs.AsNoTracking()
                .Where(bom => bom.ProductId == order.ProductId);
            bomQuery = string.IsNullOrWhiteSpace(order.BomVersion)
                ? bomQuery.Where(bom => bom.IsActive)
                : bomQuery.Where(bom => bom.Version == order.BomVersion);
            selectedBomMaterialCost = await bomQuery
                .OrderByDescending(bom => bom.Id)
                .Select(bom => (decimal?)bom.TotalMaterialCost)
                .FirstOrDefaultAsync();
        }

        Routing? selectedRouting = null;
        if (!targetSnapshotPresent || !actualSnapshotPresent)
        {
            var routingQuery = _context.Routings.AsNoTracking()
                .Include(routing => routing.Steps)
                    .ThenInclude(step => step.WorkCenter)
                .Where(routing => routing.ProductId == order.ProductId);
            routingQuery = string.IsNullOrWhiteSpace(order.RoutingVersion)
                ? routingQuery.Where(routing => routing.IsActive)
                : routingQuery.Where(routing => routing.Version == order.RoutingVersion);
            selectedRouting = await routingQuery
                .OrderByDescending(routing => routing.Id)
                .FirstOrDefaultAsync();
        }
        decimal? actualUnitCostSnapshot = null;
        if (actualSnapshotPresent)
        {
            actualUnitCostSnapshot = await _context.Lots.AsNoTracking()
                .Where(lot => lot.WorkOrderId == order.Id)
                .OrderByDescending(lot => lot.Id)
                .Select(lot => (decimal?)lot.UnitPrice)
                .FirstOrDefaultAsync();
        }

        return View(new WorkOrderDetailsViewModel
        {
            Order = order,
            Reservations = reservations,
            CostAnalysis = BuildCostAnalysis(
                order,
                reservations,
                selectedBomMaterialCost ?? 0m,
                selectedRouting,
                actualUnitCostSnapshot)
        });
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
                var result = await SaveDailyLogAsync(id);
                if (result == DailyLogSaveResult.Saved)
                    await NotifyProgressAsync();
                return DailyLogResult(result, id);
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
            {
                await transaction.CommitAsync();
                await NotifyProgressAsync();
            }
            else
                await transaction.RollbackAsync();

            return DailyLogResult(result, id);
        }
        catch (Exception ex)
        {
            try
            {
                await transaction.RollbackAsync();
            }
            catch
            {
                // SQL Server may already have rolled back a deadlock victim.
            }
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

        async Task NotifyProgressAsync()
        {
            if (_productionHub is null)
                return;

            try
            {
                await _productionHub.Clients.All.SendAsync("ReceiveProgressUpdate");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Daily production log {WorkOrderId} was committed but realtime notification failed.",
                    id);
            }
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
        else if (input.Date is not null &&
                 input.Date.Value.Date > GetBusinessDate() &&
                 !HasModelError(nameof(input.Date)))
        {
            ModelState.AddModelError(
                nameof(input.Date),
                "Ngày sản xuất không được ở tương lai.");
        }
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

    private static ProductionCostAnalysisViewModel BuildCostAnalysis(
        WorkOrder order,
        IReadOnlyCollection<MaterialReservation> reservations,
        decimal targetMaterialCostPerUnit,
        Routing? activeRouting,
        decimal? actualUnitCostSnapshot)
    {
        var plannedQuantity = order.Qty > 0m ? order.Qty : 0m;
        var targetSnapshotPresent =
            order.TargetMaterialCost.HasValue &&
            order.TargetLaborCost.HasValue &&
            order.TargetMachineCost.HasValue;
        var targetMaterialCost = order.TargetMaterialCost ??
            targetMaterialCostPerUnit * plannedQuantity;
        var targetLaborCost = order.TargetLaborCost ??
            (activeRouting?.Steps.Sum(step =>
                step.StandardTimeMinutes / 60m *
                (step.WorkCenter?.HourlyLaborRate ?? 0m)) ?? 0m) * plannedQuantity;
        var targetMachineCost = order.TargetMachineCost ??
            (activeRouting?.Steps.Sum(step =>
                step.StandardTimeMinutes / 60m *
                (step.WorkCenter?.HourlyMachineRate ?? 0m)) ?? 0m) * plannedQuantity;

        var actualSnapshotPresent =
            order.ActualMaterialCost.HasValue &&
            order.ActualLaborCost.HasValue &&
            order.ActualMachineCost.HasValue;
        var actualMaterialCost = order.ActualMaterialCost ?? reservations.Sum(reservation =>
            reservation.QtyReserved * (reservation.Lot?.UnitPrice ?? 0m));
        var actualLaborCost = order.ActualLaborCost ?? 0m;
        var actualMachineCost = order.ActualMachineCost ?? 0m;
        if (!actualSnapshotPresent)
        {
            foreach (var step in order.Steps)
            {
                if (step.WorkCenter is null)
                {
                    continue;
                }

                var durationMinutes = step.StartTime.HasValue && step.EndTime.HasValue
                    ? (decimal)(step.EndTime.Value - step.StartTime.Value).TotalMinutes
                    : 0m;
                if (durationMinutes <= 0m)
                {
                    var standardTimeMinutes = activeRouting?.Steps
                        .Where(routingStep => routingStep.StepNumber == step.StepNumber)
                        .OrderByDescending(routingStep => routingStep.Id)
                        .Select(routingStep => routingStep.StandardTimeMinutes)
                        .FirstOrDefault() ?? 0m;
                    durationMinutes = standardTimeMinutes > 0m
                        ? standardTimeMinutes
                        : 0m;
                }

                actualLaborCost += durationMinutes / 60m *
                    step.WorkCenter.HourlyLaborRate;
                actualMachineCost += durationMinutes / 60m *
                    step.WorkCenter.HourlyMachineRate;
            }
        }

        var rawTargetTotalCost = targetMaterialCost + targetLaborCost + targetMachineCost;
        var rawActualTotalCost = actualMaterialCost + actualLaborCost + actualMachineCost;
        var targetBreakdown = targetSnapshotPresent
            ? new ProductionCostBreakdown(
                targetMaterialCost,
                targetLaborCost,
                targetMachineCost,
                rawTargetTotalCost)
            : ProductionCostBreakdown.FromRaw(
                targetMaterialCost,
                targetLaborCost,
                targetMachineCost);
        var actualBreakdown = actualSnapshotPresent
            ? new ProductionCostBreakdown(
                actualMaterialCost,
                actualLaborCost,
                actualMachineCost,
                rawActualTotalCost)
            : ProductionCostBreakdown.FromRaw(
                actualMaterialCost,
                actualLaborCost,
                actualMachineCost);
        var materialComparison = new CostComparisonViewModel(
            targetBreakdown.Material,
            actualBreakdown.Material);
        var laborComparison = new CostComparisonViewModel(
            targetBreakdown.Labor,
            actualBreakdown.Labor);
        var machineComparison = new CostComparisonViewModel(
            targetBreakdown.Machine,
            actualBreakdown.Machine);
        var targetTotalCost = targetBreakdown.Total;
        var actualTotalCost = actualBreakdown.Total;
        var finishedOutputQuantity = order.Steps
            .OrderByDescending(step => step.StepNumber)
            .ThenByDescending(step => step.Id)
            .Select(step => step.QtyOK)
            .FirstOrDefault();
        var targetUnitCost = plannedQuantity > 0m
            ? rawTargetTotalCost / plannedQuantity
            : 0m;
        var actualUnitCost = actualUnitCostSnapshot ??
            (finishedOutputQuantity > 0m
            ? rawActualTotalCost / finishedOutputQuantity
            : 0m);

        return new ProductionCostAnalysisViewModel
        {
            MaterialCost = materialComparison,
            LaborCost = laborComparison,
            MachineCost = machineComparison,
            TotalCost = new CostComparisonViewModel(targetTotalCost, actualTotalCost),
            UnitCost = new CostComparisonViewModel(targetUnitCost, actualUnitCost)
        };
    }

    private enum DailyLogSaveResult
    {
        Missing,
        WrongStatus,
        Saved
    }
}
