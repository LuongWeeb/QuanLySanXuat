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

[Authorize(Roles = "Admin,QC,Manager")]
public class QcController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IQcService _qcService;
    private readonly ILogger<QcController> _logger;

    public QcController(ApplicationDbContext context, IQcService qcService, ILogger<QcController> logger) => (_context, _qcService, _logger) = (context, qcService, logger);

    public async Task<IActionResult> Index()
    {
        var lots = await _context.StockBalances.AsNoTracking().Where(x => x.QtyOnHold > 0 && x.Location!.Code != QcService.QuarantineLocationCode)
            .Select(x => x.Lot!).Distinct().Include(x => x.Product).OrderBy(x => x.LotNo).ToListAsync();
        return View(lots);
    }

    [HttpGet]
    public async Task<IActionResult> Inspect(int lotId)
    {
        var loaded = await LoadAsync(lotId);
        return loaded is null ? NotFound() : View(loaded.Value.Model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Inspect(QcInspectionInputModel input)
    {
        var loaded = await LoadAsync(input.LotId);
        if (loaded is null) return NotFound();
        var (model, lot, checklist) = loaded.Value;

        if (input.ChecklistId != checklist.Id) ModelState.AddModelError(nameof(input.ChecklistId), "Bộ tiêu chí không hợp lệ hoặc không còn hoạt động.");
        if (input.Result is not QCResult.PASS and not QCResult.REJECT) ModelState.AddModelError(nameof(input.Result), "Kết quả chỉ có thể là PASS hoặc REJECT.");
        var values = input.Measurements.GroupBy(x => x.ChecklistItemId).ToDictionary(x => x.Key, x => x.First().Value);
        if (input.Measurements.Count != values.Count || input.Measurements.Any(x => checklist.Items.All(i => i.Id != x.ChecklistItemId)))
            ModelState.AddModelError(nameof(input.Measurements), "Thông số kiểm tra không hợp lệ.");
        foreach (var item in checklist.Items.Where(x => x.IsRequired))
            if (!values.TryGetValue(item.Id, out var value) || string.IsNullOrWhiteSpace(value)) ModelState.AddModelError(nameof(input.Measurements), $"{item.ParameterName} là bắt buộc.");
        foreach (var item in checklist.Items.Where(x => x.MinVal.HasValue || x.MaxVal.HasValue))
            if (values.TryGetValue(item.Id, out var value) && !string.IsNullOrWhiteSpace(value) && !decimal.TryParse(value, out _))
                ModelState.AddModelError(nameof(input.Measurements), $"{item.ParameterName} phải là giá trị số.");

        if (!ModelState.IsValid)
        {
            model.Result = input.Result; model.Note = input.Note;
            foreach (var measurement in model.Measurements) if (values.TryGetValue(measurement.ChecklistItemId, out var value)) measurement.Value = value;
            return View(model);
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            TempData["StatusMessage"] = "Không thể xác định danh tính người kiểm định. Vui lòng đăng nhập lại.";
            return RedirectToAction(nameof(Inspect), new { lotId = lot.Id });
        }

        var inspection = new QCInspection { LotId = lot.Id, WorkOrderId = lot.WorkOrderId!.Value, Result = input.Result, Note = input.Note?.Trim() ?? string.Empty,
            Lines = checklist.Items.Where(item => !string.IsNullOrWhiteSpace(values.GetValueOrDefault(item.Id)))
                .Select(item => new QCInspectionLine { ParameterName = item.ParameterName, ValueInspected = values[item.Id].Trim() }).ToList() };
        try
        {
            var success = await _qcService.SubmitQCInspectionAsync(inspection, userId);
            TempData["StatusMessage"] = success ? $"Đã lưu kết quả kiểm định lô {lot.LotNo}." : "Không thể lưu kết quả kiểm định. Lô có thể không còn ở trạng thái chờ QC.";
            return success ? RedirectToAction(nameof(Index)) : RedirectToAction(nameof(Inspect), new { lotId = lot.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed QC inspection for lot {LotId}.", lot.Id);
            TempData["StatusMessage"] = "Không thể lưu kết quả kiểm định. Vui lòng thử lại hoặc liên hệ quản trị viên.";
            return RedirectToAction(nameof(Inspect), new { lotId = lot.Id });
        }
    }

    private async Task<(QcInspectionInputModel Model, Lot Lot, QCChecklist Checklist)?> LoadAsync(int lotId)
    {
        var lot = await _context.Lots.AsNoTracking().Include(x => x.Product).SingleOrDefaultAsync(x => x.Id == lotId && x.WorkOrderId != null);
        if (lot is null || !await _context.StockBalances.AnyAsync(x => x.LotId == lotId && x.QtyOnHold > 0 && x.Location!.Code != QcService.QuarantineLocationCode)) return null;
        var checklist = await _context.QCChecklists.AsNoTracking().Include(x => x.Items).Where(x => x.ProductId == lot.ProductId && x.IsActive).OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        if (checklist is null || checklist.Items.Count == 0) return null;
        var model = new QcInspectionInputModel { LotId=lot.Id, ChecklistId=checklist.Id, LotNo=lot.LotNo, ProductDisplay=$"{lot.Product?.Code} - {lot.Product?.Name}", ChecklistName=checklist.Name,
            Measurements=checklist.Items.OrderBy(x=>x.Id).Select(x=>new QcMeasurementInputModel {ChecklistItemId=x.Id,ParameterName=x.ParameterName,MinVal=x.MinVal,MaxVal=x.MaxVal,Unit=x.Unit,IsRequired=x.IsRequired}).ToList() };
        return (model, lot, checklist);
    }
}
