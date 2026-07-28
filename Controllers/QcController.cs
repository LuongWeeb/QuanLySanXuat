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
    private readonly IWebHostEnvironment? _environment;

    public QcController(
        ApplicationDbContext context,
        IQcService qcService,
        ILogger<QcController> logger,
        IWebHostEnvironment? environment = null) =>
        (_context, _qcService, _logger, _environment) =
        (context, qcService, logger, environment);

    public async Task<IActionResult> Index()
    {
        return View(await GetPendingLotsAsync());
    }

    public async Task<IActionResult> Pending()
    {
        return View(await GetEligiblePendingLotsAsync());
    }

    [HttpGet]
    public async Task<IActionResult> Inspect(int lotId)
    {
        var loaded = await LoadAsync(lotId);
        return loaded is null ? NotFound() : View(loaded.Value.Model);
    }

    [HttpGet]
    public async Task<IActionResult> CreateInspection(int lotId)
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
        var (model, lot, checklist, goodsReceiptId, type) = loaded.Value;

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
        ValidateEvidence(input.EvidenceFile);

        if (!ModelState.IsValid)
        {
            model.Result = input.Result; model.Note = input.Note; model.EvidencePath = input.EvidencePath;
            foreach (var measurement in model.Measurements) if (values.TryGetValue(measurement.ChecklistItemId, out var value)) measurement.Value = value;
            return View(model);
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            TempData["StatusMessage"] = "Không thể xác định danh tính người kiểm định. Vui lòng đăng nhập lại.";
            return RedirectToAction(nameof(Inspect), new { lotId = lot.Id });
        }

        var evidencePath = await SaveEvidenceAsync(input.EvidenceFile);
        var inspection = new QCInspection { LotId = lot.Id, WorkOrderId = lot.WorkOrderId, GoodsReceiptId = goodsReceiptId, Type = type, Result = input.Result, Note = input.Note?.Trim() ?? string.Empty, EvidencePath = evidencePath,
            Lines = checklist.Items.Where(item => !string.IsNullOrWhiteSpace(values.GetValueOrDefault(item.Id)))
                .Select(item => new QCInspectionLine { ParameterName = item.ParameterName, ValueInspected = values[item.Id].Trim() }).ToList() };
        try
        {
            var success = await _qcService.SubmitQCInspectionAsync(inspection, userId);
            if (!success) DeleteEvidence(evidencePath);
            TempData["StatusMessage"] = success ? $"Đã lưu kết quả kiểm định lô {lot.LotNo}." : "Không thể lưu kết quả kiểm định. Lô có thể không còn ở trạng thái chờ QC.";
            return success ? RedirectToAction(nameof(Index)) : RedirectToAction(nameof(Inspect), new { lotId = lot.Id });
        }
        catch (Exception ex)
        {
            DeleteEvidence(evidencePath);
            _logger.LogError(ex, "Failed QC inspection for lot {LotId}.", lot.Id);
            TempData["StatusMessage"] = "Không thể lưu kết quả kiểm định. Vui lòng thử lại hoặc liên hệ quản trị viên.";
            return RedirectToAction(nameof(Inspect), new { lotId = lot.Id });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> CreateInspection(QcInspectionInputModel input)
    {
        return Inspect(input);
    }

    public async Task<IActionResult> Details(int id)
    {
        var inspection = await _context.QCInspections
            .AsNoTracking()
            .Include(item => item.Lot)
                .ThenInclude(lot => lot!.Product)
            .Include(item => item.WorkOrder)
            .Include(item => item.GoodsReceipt)
            .Include(item => item.Lines)
            .SingleOrDefaultAsync(item => item.Id == id);
        return inspection is null ? NotFound() : View(inspection);
    }

    private async Task<(QcInspectionInputModel Model, Lot Lot, QCChecklist Checklist, int? GoodsReceiptId, QCInspectionType Type)?> LoadAsync(int lotId)
    {
        var lot = await _context.Lots.AsNoTracking().Include(x => x.Product).SingleOrDefaultAsync(x => x.Id == lotId);
        if (lot is null || !await _context.StockBalances.AnyAsync(x => x.LotId == lotId && x.QtyOnHold > 0 && x.Location!.Code != QcService.QuarantineLocationCode)) return null;
        var goodsReceiptId = lot.WorkOrderId.HasValue
            ? null
            : await _context.GoodsReceiptLines
                .Where(line => line.ProductId == lot.ProductId &&
                    line.LotNo == lot.LotNo &&
                    line.GoodsReceipt!.Status == DocumentStatus.Completed)
                .OrderByDescending(line => line.GoodsReceipt!.ReceiptDate)
                .ThenByDescending(line => line.GoodsReceiptId)
                .Select(line => (int?)line.GoodsReceiptId)
                .FirstOrDefaultAsync();
        if (!lot.WorkOrderId.HasValue && !goodsReceiptId.HasValue) return null;
        var checklist = await _context.QCChecklists.AsNoTracking().Include(x => x.Items).Where(x => x.ProductId == lot.ProductId && x.IsActive).OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        if (checklist is null || checklist.Items.Count == 0) return null;
        var model = new QcInspectionInputModel { LotId=lot.Id, ChecklistId=checklist.Id, LotNo=lot.LotNo, ProductDisplay=$"{lot.Product?.Code} - {lot.Product?.Name}", ChecklistName=checklist.Name,
            Measurements=checklist.Items.OrderBy(x=>x.Id).Select(x=>new QcMeasurementInputModel {ChecklistItemId=x.Id,ParameterName=x.ParameterName,MinVal=x.MinVal,MaxVal=x.MaxVal,Unit=x.Unit,IsRequired=x.IsRequired}).ToList() };
        return (
            model,
            lot,
            checklist,
            goodsReceiptId,
            lot.WorkOrderId.HasValue
                ? QCInspectionType.FinalFGQC
                : QCInspectionType.InwardQC);
    }

    private Task<List<Lot>> GetPendingLotsAsync()
    {
        return _context.StockBalances
            .AsNoTracking()
            .Where(item => item.QtyOnHold > 0 &&
                item.Location!.Code != QcService.QuarantineLocationCode)
            .Select(item => item.Lot!)
            .Distinct()
            .Include(item => item.Product)
            .OrderBy(item => item.LotNo)
            .ToListAsync();
    }

    private Task<List<QcPendingLotViewModel>> GetEligiblePendingLotsAsync()
    {
        return _context.Lots
            .AsNoTracking()
            .Where(lot =>
                _context.StockBalances.Any(balance =>
                    balance.LotId == lot.Id &&
                    balance.QtyOnHold > 0 &&
                    balance.Location!.Code != QcService.QuarantineLocationCode) &&
                _context.QCChecklists.Any(checklist =>
                    checklist.ProductId == lot.ProductId &&
                    checklist.IsActive &&
                    checklist.Items.Any()) &&
                (lot.WorkOrderId.HasValue ||
                    _context.GoodsReceiptLines.Any(line =>
                        line.ProductId == lot.ProductId &&
                        line.LotNo == lot.LotNo &&
                        line.GoodsReceipt!.Status == DocumentStatus.Completed)))
            .OrderBy(lot => lot.LotNo)
            .Select(lot => new QcPendingLotViewModel
            {
                LotId = lot.Id,
                LotNo = lot.LotNo,
                ProductDisplay = lot.Product!.Code + " — " + lot.Product.Name,
                Type = lot.WorkOrderId.HasValue
                    ? QCInspectionType.FinalFGQC
                    : QCInspectionType.InwardQC,
                QtyOnHold = _context.StockBalances
                    .Where(balance =>
                        balance.LotId == lot.Id &&
                        balance.Location!.Code != QcService.QuarantineLocationCode)
                    .Sum(balance => balance.QtyOnHold)
            })
            .ToListAsync();
    }

    private void ValidateEvidence(IFormFile? file)
    {
        if (file is null || file.Length == 0) return;
        var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
        if (file.Length > 5 * 1024 * 1024)
            ModelState.AddModelError(nameof(QcInspectionInputModel.EvidenceFile), "Ảnh bằng chứng không được vượt quá 5 MB.");
        if (!allowedTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
            ModelState.AddModelError(nameof(QcInspectionInputModel.EvidenceFile), "Ảnh bằng chứng phải là JPG, PNG hoặc WebP.");
    }

    private async Task<string> SaveEvidenceAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0) return string.Empty;
        var extension = file.ContentType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => throw new InvalidOperationException("Unsupported evidence image type.")
        };
        var webRoot = _environment?.WebRootPath ??
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var directory = Path.Combine(webRoot, "uploads", "qc");
        Directory.CreateDirectory(directory);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        await using var stream = System.IO.File.Create(Path.Combine(directory, fileName));
        await file.CopyToAsync(stream);
        return $"/uploads/qc/{fileName}";
    }

    private void DeleteEvidence(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var webRoot = _environment?.WebRootPath ??
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var fullPath = Path.Combine(
            webRoot,
            path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
    }
}
