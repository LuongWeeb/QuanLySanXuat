using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.DTOs;
using WmsMes.Web.Services;
using WmsMes.Web.ViewModels;

namespace WmsMes.Web.Controllers;

[Authorize]
public class InventoryController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IInventoryService? _inventoryService;
    private readonly IReportExportService _reportExportService;
    private readonly ILogger<InventoryController> _logger;

    public InventoryController(
        ApplicationDbContext context,
        IReportExportService reportExportService,
        IInventoryService? inventoryService = null,
        ILogger<InventoryController>? logger = null)
    {
        _context = context;
        _inventoryService = inventoryService;
        _reportExportService = reportExportService ?? throw new ArgumentNullException(nameof(reportExportService));
        _logger = logger ?? NullLogger<InventoryController>.Instance;
    }

    public async Task<IActionResult> Index()
    {
        var balances = await _context.StockBalances
            .Include(sb => sb.Product)
            .Include(sb => sb.Lot)
            .Include(sb => sb.Location)
            .ThenInclude(location => location!.Zone)
            .OrderBy(sb => sb.Product!.Code)
            .ThenBy(sb => sb.Lot!.ExpiryDate)
            .ThenBy(sb => sb.Location!.Code)
            .AsNoTracking()
            .ToListAsync();

        return View(balances);
    }

    [HttpGet("[controller]/[action]")]
    public async Task<IActionResult> ExportExcel(int? warehouseId)
    {
        var bytes = await _reportExportService.ExportStockBalanceToExcelAsync(warehouseId);
        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"TonKho_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    [HttpGet("api/inventory/picking-recommendations")]
    public async Task<IActionResult> GetPickingRecommendations(int productId, decimal requiredQty, PickingStrategy strategy = PickingStrategy.FEFO)
    {
        if (_inventoryService is null)
        {
            throw new InvalidOperationException("IInventoryService is required to get picking recommendations.");
        }

        var result = await _inventoryService.GetPickingRecommendationsAsync(productId, requiredQty, strategy);
        return Ok(result);
    }

    [Authorize(Roles = "Admin,Warehouse,Manager")]
    public async Task<IActionResult> Receipts()
    {
        var receipts = await _context.GoodsReceipts
            .Include(receipt => receipt.Supplier)
            .Include(receipt => receipt.Lines)
                .ThenInclude(line => line.Product)
            .Include(receipt => receipt.Lines)
                .ThenInclude(line => line.Location)
            .OrderByDescending(receipt => receipt.ReceiptDate)
            .AsNoTracking()
            .ToListAsync();

        return View(receipts);
    }

    [Authorize(Roles = "Admin,Warehouse,Manager")]
    public async Task<IActionResult> Issues()
    {
        var issues = await _context.GoodsIssues
            .Include(issue => issue.Customer)
            .Include(issue => issue.Lines)
                .ThenInclude(line => line.Product)
            .Include(issue => issue.Lines)
                .ThenInclude(line => line.Lot)
            .Include(issue => issue.Lines)
                .ThenInclude(line => line.Location)
            .OrderByDescending(issue => issue.IssueDate)
            .AsNoTracking()
            .ToListAsync();

        return View(issues);
    }

    [HttpGet]
    [Authorize(Roles = "Admin,Warehouse,Manager")]
    public async Task<IActionResult> CreateIssue()
    {
        await LoadIssueSelectionsAsync();
        return View(new CreateIssueViewModel { Lines = { new IssueLineInput() } });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin,Warehouse,Manager")]
    public async Task<IActionResult> CreateIssue(CreateIssueViewModel model)
    {
        model.Lines ??= new List<IssueLineInput>();
        var userId = HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            ModelState.AddModelError(string.Empty, "Không xác định được người dùng đang đăng nhập.");

        var customerIsActive = await _context.Customers
            .AsNoTracking()
            .AnyAsync(customer => customer.Id == model.CustomerId && customer.IsActive);
        if (!customerIsActive)
        {
            ModelState.AddModelError(nameof(model.CustomerId), "Khách hàng không hợp lệ hoặc đã ngừng hoạt động.");
        }

        if (model.Lines.Count == 0)
        {
            ModelState.AddModelError(nameof(model.Lines), "Phiếu xuất kho phải có ít nhất một dòng.");
        }

        for (var index = 0; index < model.Lines.Count; index++)
        {
            var line = model.Lines[index];
            var keyPrefix = $"Lines[{index}]";
            var productIsActive = await _context.Products
                .AsNoTracking()
                .AnyAsync(product => product.Id == line.ProductId && product.IsActive);
            if (!productIsActive)
            {
                ModelState.AddModelError($"{keyPrefix}.{nameof(line.ProductId)}", "Sản phẩm không hợp lệ hoặc đã ngừng hoạt động.");
            }

            var lotMatchesProduct = await _context.Lots
                .AsNoTracking()
                .AnyAsync(lot => lot.Id == line.LotId && lot.ProductId == line.ProductId);
            if (!lotMatchesProduct)
            {
                ModelState.AddModelError($"{keyPrefix}.{nameof(line.LotId)}", "Lô hàng không thuộc sản phẩm đã chọn.");
            }

            var locationIsActive = await _context.Locations
                .AsNoTracking()
                .AnyAsync(location => location.Id == line.LocationId && location.IsActive);
            if (!locationIsActive)
            {
                ModelState.AddModelError($"{keyPrefix}.{nameof(line.LocationId)}", "Vị trí không hợp lệ hoặc đã ngừng hoạt động.");
            }

            if (line.Qty <= 0)
            {
                ModelState.AddModelError($"{keyPrefix}.{nameof(line.Qty)}", "Số lượng phải lớn hơn 0.");
            }
            else
            {
                var qtyAvailable = await _context.StockBalances
                    .AsNoTracking()
                    .Where(balance =>
                        balance.ProductId == line.ProductId &&
                        balance.LotId == line.LotId &&
                        balance.LocationId == line.LocationId)
                    .Select(balance => (decimal?)balance.QtyAvailable)
                    .FirstOrDefaultAsync();
                if (qtyAvailable is null || qtyAvailable < line.Qty)
                {
                    ModelState.AddModelError($"{keyPrefix}.{nameof(line.Qty)}", "Số lượng xuất vượt quá tồn kho khả dụng của lô tại vị trí đã chọn.");
                }
            }
        }

        if (!ModelState.IsValid)
        {
            if (model.Lines.Count == 0)
            {
                model.Lines.Add(new IssueLineInput());
            }
            await LoadIssueSelectionsAsync();
            return View(model);
        }

        if (_inventoryService is null)
        {
            throw new InvalidOperationException("IInventoryService is required to complete a goods issue.");
        }

        var issue = new GoodsIssue
        {
            IssueNo = $"GI-{DateTime.UtcNow:yyyyMMddHHmmssfff}",
            IssueDate = DateTime.UtcNow,
            Status = DocumentStatus.Draft,
            CustomerId = model.CustomerId,
            Lines = model.Lines.Select(line => new GoodsIssueLine
            {
                ProductId = line.ProductId,
                LotId = line.LotId,
                Qty = line.Qty,
                LocationId = line.LocationId
            }).ToList()
        };

        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync()
            : null;

        try
        {
            _context.GoodsIssues.Add(issue);
            await _context.SaveChangesAsync();
            if (!await _inventoryService.CompleteGoodsIssueWithoutNotificationAsync(issue.Id, userId!))
            {
                await RollbackIssueAsync(issue, transaction);
                return await IssueCompletionErrorAsync(model, "Không thể hoàn tất phiếu xuất kho. Vui lòng thử lại.");
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync();
            }

            await NotifyAfterCommitAsync();

            TempData["StatusMessage"] = $"Đã xuất kho {model.Lines.Sum(line => line.Qty):N2} thành công.";
            return RedirectToAction(nameof(Issues));
        }
        catch (Exception)
        {
            await RollbackIssueAsync(issue, transaction);
            return await IssueCompletionErrorAsync(model, "Có lỗi khi hoàn tất phiếu xuất kho. Vui lòng thử lại.");
        }
    }

    [HttpGet]
    [Authorize(Roles = "Admin,Warehouse,Manager")]
    public async Task<IActionResult> CreateReceipt()
    {
        await LoadReceiptSelectionsAsync();
        return View(new CreateReceiptViewModel { Lines = { new ReceiptLineInput() } });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin,Warehouse,Manager")]
    public async Task<IActionResult> CreateReceipt(CreateReceiptViewModel model)
    {
        model.Lines ??= new List<ReceiptLineInput>();
        var userId = HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            ModelState.AddModelError(string.Empty, "Không xác định được người dùng đang đăng nhập.");
        if (!await _context.Suppliers.AsNoTracking().AnyAsync(x => x.Id == model.SupplierId && x.IsActive))
            ModelState.AddModelError(nameof(model.SupplierId), "Nhà cung cấp không hợp lệ hoặc đã ngừng hoạt động.");
        if (model.Lines.Count == 0)
            ModelState.AddModelError(nameof(model.Lines), "Phiếu nhập kho phải có ít nhất một dòng.");

        for (var index = 0; index < model.Lines.Count; index++)
        {
            var line = model.Lines[index];
            var keyPrefix = $"Lines[{index}]";
            if (!await _context.Products.AsNoTracking().AnyAsync(x => x.Id == line.ProductId && x.IsActive))
                ModelState.AddModelError($"{keyPrefix}.{nameof(line.ProductId)}", "Sản phẩm không hợp lệ hoặc đã ngừng hoạt động.");
            if (!await _context.Locations.AsNoTracking().AnyAsync(x => x.Id == line.LocationId && x.IsActive))
                ModelState.AddModelError($"{keyPrefix}.{nameof(line.LocationId)}", "Vị trí không hợp lệ hoặc đã ngừng hoạt động.");
            if (string.IsNullOrWhiteSpace(line.LotNo))
                ModelState.AddModelError($"{keyPrefix}.{nameof(line.LotNo)}", "Số lô là bắt buộc.");
            if (line.Qty <= 0)
                ModelState.AddModelError($"{keyPrefix}.{nameof(line.Qty)}", "Số lượng phải lớn hơn 0.");
            if (line.UnitPrice < 0)
                ModelState.AddModelError($"{keyPrefix}.{nameof(line.UnitPrice)}", "Đơn giá không được âm.");
        }

        if (!ModelState.IsValid)
        {
            if (model.Lines.Count == 0)
            {
                model.Lines.Add(new ReceiptLineInput());
            }
            await LoadReceiptSelectionsAsync();
            return View(model);
        }

        var receipt = new GoodsReceipt
        {
            ReceiptNo = $"GR-{DateTime.UtcNow:yyyyMMddHHmmssfff}",
            ReceiptDate = DateTime.UtcNow,
            SupplierId = model.SupplierId,
            Status = DocumentStatus.Draft,
            Lines = model.Lines.Select(line => new GoodsReceiptLine
            {
                ProductId = line.ProductId,
                LotNo = line.LotNo.Trim(),
                Qty = line.Qty,
                UnitPrice = line.UnitPrice,
                LocationId = line.LocationId
            }).ToList()
        };

        if (_inventoryService is null)
        {
            throw new InvalidOperationException("IInventoryService is required to complete a goods receipt.");
        }

        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync()
            : null;

        try
        {
            _context.GoodsReceipts.Add(receipt);
            await _context.SaveChangesAsync();

            var completed = await _inventoryService.CompleteGoodsReceiptWithoutNotificationAsync(receipt.Id, userId!);
            if (!completed)
            {
                await RollbackReceiptAsync(receipt, transaction);
                return await ReceiptCompletionErrorAsync(model, "Không thể hoàn tất phiếu nhập kho. Vui lòng thử lại.");
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync();
            }

            await NotifyAfterCommitAsync();

            TempData["StatusMessage"] = $"Đã nhập kho {model.Lines.Count} dòng hàng thành công.";
            return RedirectToAction(nameof(Receipts));
        }
        catch (Exception)
        {
            await RollbackReceiptAsync(receipt, transaction);
            return await ReceiptCompletionErrorAsync(model, "Có lỗi khi hoàn tất phiếu nhập kho. Vui lòng thử lại.");
        }
    }

    private async Task LoadReceiptSelectionsAsync()
    {
        ViewBag.Products = await _context.Products
            .Where(product => product.IsActive)
            .OrderBy(product => product.Code)
            .AsNoTracking()
            .ToListAsync();
        ViewBag.Suppliers = await _context.Suppliers
            .Where(supplier => supplier.IsActive)
            .OrderBy(supplier => supplier.Name)
            .AsNoTracking()
            .ToListAsync();
        ViewBag.Locations = await _context.Locations
            .Where(location => location.IsActive)
            .OrderBy(location => location.Code)
            .AsNoTracking()
            .ToListAsync();
    }

    private async Task LoadIssueSelectionsAsync()
    {
        ViewBag.AvailableBalances = await _context.StockBalances
            .Include(balance => balance.Product)
            .Include(balance => balance.Lot)
            .Include(balance => balance.Location)
            .Where(balance => balance.QtyAvailable > 0 && balance.Product!.IsActive && balance.Location!.IsActive)
            .OrderBy(balance => balance.Product!.Code)
            .ThenBy(balance => balance.Product!.ShelfLifeDays.HasValue
                ? balance.Lot!.ExpiryDate ?? DateTime.MaxValue
                : DateTime.MinValue)
            .ThenBy(balance => balance.LotId)
            .ThenBy(balance => balance.Location!.Code)
            .AsNoTracking()
            .ToListAsync();
        ViewBag.Customers = await _context.Customers
            .Where(customer => customer.IsActive)
            .OrderBy(customer => customer.Code)
            .AsNoTracking()
            .ToListAsync();
    }

    private async Task RollbackReceiptAsync(GoodsReceipt receipt, IDbContextTransaction? transaction)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync();
            _context.ChangeTracker.Clear();
            return;
        }

        _context.GoodsReceipts.Remove(receipt);
        await _context.SaveChangesAsync();
    }

    private async Task RollbackIssueAsync(GoodsIssue issue, IDbContextTransaction? transaction)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync();
            _context.ChangeTracker.Clear();
            return;
        }

        _context.GoodsIssues.Remove(issue);
        await _context.SaveChangesAsync();
    }

    private async Task<IActionResult> ReceiptCompletionErrorAsync(CreateReceiptViewModel model, string message)
    {
        ModelState.AddModelError(string.Empty, message);
        await LoadReceiptSelectionsAsync();
        return View(nameof(CreateReceipt), model);
    }

    private async Task<IActionResult> IssueCompletionErrorAsync(CreateIssueViewModel model, string message)
    {
        ModelState.AddModelError(string.Empty, message);
        await LoadIssueSelectionsAsync();
        return View(nameof(CreateIssue), model);
    }

    private async Task NotifyAfterCommitAsync()
    {
        try
        {
            await _inventoryService!.NotifyStockChangedAsync();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Inventory operation committed but realtime notification failed.");
        }
    }
}
