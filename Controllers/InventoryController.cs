using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Common;
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
    private readonly ILowStockService _lowStockService;
    private readonly INotificationService _notificationService;

    public InventoryController(
        ApplicationDbContext context,
        IReportExportService reportExportService,
        IInventoryService? inventoryService = null,
        ILogger<InventoryController>? logger = null,
        ILowStockService? lowStockService = null,
        INotificationService? notificationService = null)
    {
        _context = context;
        _inventoryService = inventoryService;
        _reportExportService = reportExportService ?? throw new ArgumentNullException(nameof(reportExportService));
        _logger = logger ?? NullLogger<InventoryController>.Instance;
        _lowStockService = lowStockService ?? new LowStockService(context);
        _notificationService = notificationService ?? new NotificationService(context);
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

        return View(new InventoryIndexViewModel
        {
            Balances = balances,
            LowStockItems = await _lowStockService.GetLowStockItemsAsync()
        });
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
    public async Task<IActionResult> Transactions(
        DateTime? beforeDate = null,
        int? beforeId = null)
    {
        const int pageSize = 50;
        var requestQuery = ControllerContext.HttpContext?.Request.Query;
        var beforeDateSupplied = requestQuery?.ContainsKey(nameof(beforeDate))
            ?? beforeDate.HasValue;
        var beforeIdSupplied = requestQuery?.ContainsKey(nameof(beforeId))
            ?? beforeId.HasValue;
        if (!ModelState.IsValid ||
            beforeDateSupplied != beforeIdSupplied ||
            (beforeDateSupplied &&
             (!beforeDate.HasValue || !beforeId.HasValue)) ||
            (beforeId.HasValue && beforeId.Value <= 0))
        {
            return BadRequest();
        }

        var hasCursor = beforeDate.HasValue && beforeId.HasValue;
        var query = _context.StockTransactions.AsNoTracking();
        if (hasCursor)
        {
            query = query.Where(transaction =>
                transaction.TransactionDate < beforeDate!.Value ||
                (transaction.TransactionDate == beforeDate.Value &&
                 transaction.Id < beforeId!.Value));
        }

        var items = await query
            .OrderByDescending(transaction => transaction.TransactionDate)
            .ThenByDescending(transaction => transaction.Id)
            .Select(transaction => new StockTransactionListItemViewModel
            {
                Id = transaction.Id,
                Type = transaction.Type,
                ProductCode = transaction.Product!.Code,
                ProductName = transaction.Product.Name,
                LotNo = transaction.Lot!.LotNo,
                LocationCode = transaction.Location!.Code,
                Qty = transaction.Qty,
                QtyAfter = transaction.QtyAfter,
                ValuationRate = transaction.ValuationRate,
                IsCancelled = transaction.IsCancelled,
                TransactionDate = transaction.TransactionDate,
                ReferenceNo = transaction.ReferenceNo
            })
            .Take(pageSize + 1)
            .ToListAsync();
        var hasNextPage = items.Count > pageSize;
        if (hasNextPage)
        {
            items.RemoveAt(pageSize);
        }

        var lastItem = hasNextPage ? items[^1] : null;
        return View(new StockTransactionPageViewModel
        {
            Items = items,
            HasNextPage = hasNextPage,
            IsFirstPage = !hasCursor,
            NextBeforeDate = lastItem?.TransactionDate,
            NextBeforeId = lastItem?.Id
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin,Warehouse,Manager")]
    public async Task<IActionResult> CancelReceipt(int id)
    {
        if (_inventoryService is null)
        {
            throw new InvalidOperationException("IInventoryService is required.");
        }

        var userId = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        try
        {
            var success = await _inventoryService.CancelGoodsReceiptAsync(id, userId ?? "system");
            if (success)
            {
                TempData["StatusMessage"] = "Đã hủy phiếu nhập kho và hoàn trả số dư thành công.";
            }
            else
            {
                TempData["ErrorMessage"] = "Không thể hủy phiếu nhập kho.";
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to cancel goods receipt {ReceiptId}.", id);
            TempData["ErrorMessage"] =
                "Không thể hủy phiếu nhập kho. Vui lòng thử lại hoặc liên hệ quản trị viên.";
        }

        return RedirectToAction(nameof(Receipts));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin,Warehouse,Manager")]
    public async Task<IActionResult> CancelIssue(int id)
    {
        if (_inventoryService is null)
        {
            throw new InvalidOperationException("IInventoryService is required.");
        }

        var userId = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        try
        {
            var success = await _inventoryService.CancelGoodsIssueAsync(id, userId ?? "system");
            if (success)
            {
                TempData["StatusMessage"] = "Đã hủy phiếu xuất kho và thu hồi số dư thành công.";
            }
            else
            {
                TempData["ErrorMessage"] = "Không thể hủy phiếu xuất kho.";
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to cancel goods issue {IssueId}.", id);
            TempData["ErrorMessage"] =
                "Không thể hủy phiếu xuất kho. Vui lòng thử lại hoặc liên hệ quản trị viên.";
        }

        return RedirectToAction(nameof(Issues));
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
        if (model.SalesOrderId.HasValue &&
            !await _context.SalesOrders.AsNoTracking().AnyAsync(order =>
                order.Id == model.SalesOrderId.Value &&
                order.CustomerId == model.CustomerId &&
                order.Status == DocumentStatus.Draft))
        {
            ModelState.AddModelError(
                nameof(model.SalesOrderId),
                "Đơn bán hàng không hợp lệ, đã hoàn tất hoặc không thuộc khách hàng đã chọn.");
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
                .AnyAsync(location =>
                    location.Id == line.LocationId &&
                    location.IsActive &&
                    location.Code != QcService.QuarantineLocationCode);
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
                    var available = qtyAvailable ?? 0m;
                    ModelState.AddModelError(
                        $"{keyPrefix}.{nameof(line.Qty)}",
                        $"Lô hàng tại vị trí đã chọn không đủ số lượng khả dụng để xuất (Chỉ còn {available.ToVietnameseNumber()}). Số lượng giữ chỗ đang được bảo vệ.");
                }
            }
        }

        var indexedIssueLines = model.Lines
            .Select((line, index) => new { Line = line, Index = index });
        foreach (var group in indexedIssueLines
            .GroupBy(item => new
            {
                item.Line.ProductId,
                item.Line.LotId,
                item.Line.LocationId
            })
            .Where(group => group.Count() > 1))
        {
            if (group.Any(item =>
                HasFieldError(item.Index, nameof(item.Line.ProductId)) ||
                HasFieldError(item.Index, nameof(item.Line.LotId)) ||
                HasFieldError(item.Index, nameof(item.Line.LocationId)) ||
                HasFieldError(item.Index, nameof(item.Line.Qty))))
            {
                continue;
            }

            var requestedQty = group.Sum(item => item.Line.Qty);
            var qtyAvailable = await _context.StockBalances
                .AsNoTracking()
                .Where(balance =>
                    balance.ProductId == group.Key.ProductId &&
                    balance.LotId == group.Key.LotId &&
                    balance.LocationId == group.Key.LocationId &&
                    balance.Location!.Code != QcService.QuarantineLocationCode)
                .Select(balance => (decimal?)balance.QtyAvailable)
                .FirstOrDefaultAsync() ?? 0m;
            if (requestedQty <= qtyAvailable)
            {
                continue;
            }

            var cumulativeQty = 0m;
            foreach (var item in group.OrderBy(item => item.Index))
            {
                cumulativeQty += item.Line.Qty;
                if (cumulativeQty <= qtyAvailable)
                {
                    continue;
                }

                ModelState.AddModelError(
                    $"Lines[{item.Index}].{nameof(item.Line.Qty)}",
                    $"Tổng số lượng yêu cầu cho cùng lô và vị trí là {requestedQty.ToVietnameseNumber()}, vượt quá số lượng khả dụng {qtyAvailable.ToVietnameseNumber()}.");
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
            SalesOrderId = model.SalesOrderId,
            Lines = model.Lines.Select(line => new GoodsIssueLine
            {
                ProductId = line.ProductId,
                LotId = line.LotId,
                Qty = line.Qty,
                LocationId = line.LocationId,
                VarianceReason = NormalizeOptionalText(line.VarianceReason)
            }).ToList()
        };

        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable)
            : null;
        IReadOnlyList<LowStockLevelBefore> lowStockLevelsBefore = [];
        IReadOnlyList<LowStockCrossing> lowStockCrossings = [];

        try
        {
            lowStockLevelsBefore = await CaptureLowStockLevelsAsync(
                model.Lines.Select(line => line.ProductId));
            _context.GoodsIssues.Add(issue);
            await _context.SaveChangesAsync();
            if (!await _inventoryService.CompleteGoodsIssueWithoutNotificationAsync(issue.Id, userId!))
            {
                await RollbackIssueAsync(issue, transaction);
                return await IssueCompletionErrorAsync(model, "Không thể hoàn tất phiếu xuất kho. Vui lòng thử lại.");
            }

            lowStockCrossings = await CaptureLowStockCrossingsAsync(lowStockLevelsBefore);
            if (transaction is not null)
            {
                await transaction.CommitAsync();
            }
        }
        catch (Exception)
        {
            await RollbackIssueAsync(issue, transaction);
            return await IssueCompletionErrorAsync(model, "Có lỗi khi hoàn tất phiếu xuất kho. Vui lòng thử lại.");
        }

        if (transaction is not null)
        {
            try
            {
                await transaction.DisposeAsync();
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Goods issue {GoodsIssueId} committed but transaction cleanup failed.",
                    issue.Id);
            }
        }

        await NotifyAfterCommitAsync();
        await NotifyLowStockCrossingsAfterCommitAsync(lowStockCrossings);

        TempData["StatusMessage"] =
            $"Đã xuất kho {model.Lines.Sum(line => line.Qty).ToVietnameseNumber()} thành công.";
        return RedirectToAction(nameof(Issues));

        bool HasFieldError(int index, string fieldName)
        {
            var key = $"Lines[{index}].{fieldName}";
            return ModelState.TryGetValue(key, out var entry) && entry.Errors.Count > 0;
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
        if (model.PurchaseOrderId.HasValue &&
            !await _context.PurchaseOrders.AsNoTracking().AnyAsync(order =>
                order.Id == model.PurchaseOrderId.Value &&
                order.SupplierId == model.SupplierId &&
                order.Status == DocumentStatus.Draft))
        {
            ModelState.AddModelError(
                nameof(model.PurchaseOrderId),
                "Đơn mua hàng không hợp lệ, đã hoàn tất hoặc không thuộc nhà cung cấp đã chọn.");
        }
        if (model.Lines.Count == 0)
            ModelState.AddModelError(nameof(model.Lines), "Phiếu nhập kho phải có ít nhất một dòng.");

        for (var index = 0; index < model.Lines.Count; index++)
        {
            var line = model.Lines[index];
            var keyPrefix = $"Lines[{index}]";
            if (!await _context.Products.AsNoTracking().AnyAsync(x => x.Id == line.ProductId && x.IsActive))
                ModelState.AddModelError($"{keyPrefix}.{nameof(line.ProductId)}", "Sản phẩm không hợp lệ hoặc đã ngừng hoạt động.");
            if (!await _context.Locations.AsNoTracking().AnyAsync(x =>
                x.Id == line.LocationId &&
                x.IsActive &&
                x.Code != QcService.QuarantineLocationCode))
                ModelState.AddModelError($"{keyPrefix}.{nameof(line.LocationId)}", "Vị trí không hợp lệ hoặc đã ngừng hoạt động.");
            if (string.IsNullOrWhiteSpace(line.LotNo))
                ModelState.AddModelError($"{keyPrefix}.{nameof(line.LotNo)}", "Số lô là bắt buộc.");
            if (line.Qty <= 0)
                ModelState.AddModelError($"{keyPrefix}.{nameof(line.Qty)}", "Số lượng phải lớn hơn 0.");
            if (line.UnitPrice < 0)
                ModelState.AddModelError($"{keyPrefix}.{nameof(line.UnitPrice)}", "Đơn giá không được âm.");
        }

        foreach (var duplicateGroup in model.Lines
            .Select((line, index) => new { Line = line, Index = index })
            .Where(item => !string.IsNullOrWhiteSpace(item.Line.LotNo))
            .GroupBy(item => new
            {
                item.Line.ProductId,
                LotNo = item.Line.LotNo.Trim().ToUpperInvariant(),
                item.Line.LocationId
            })
            .Where(group => group.Count() > 1))
        {
            foreach (var item in duplicateGroup)
            {
                ModelState.AddModelError(
                    $"Lines[{item.Index}].{nameof(item.Line.LotNo)}",
                    "Sản phẩm, số lô và vị trí bị trùng trong phiếu nhập kho.");
            }
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
            PurchaseOrderId = model.PurchaseOrderId,
            Status = DocumentStatus.Draft,
            Lines = model.Lines.Select(line => new GoodsReceiptLine
            {
                ProductId = line.ProductId,
                LotNo = line.LotNo.Trim(),
                Qty = line.Qty,
                UnitPrice = line.UnitPrice,
                LocationId = line.LocationId,
                VarianceReason = NormalizeOptionalText(line.VarianceReason)
            }).ToList()
        };

        if (_inventoryService is null)
        {
            throw new InvalidOperationException("IInventoryService is required to complete a goods receipt.");
        }

        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable)
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
            .Where(location =>
                location.IsActive &&
                location.Code != QcService.QuarantineLocationCode)
            .OrderBy(location => location.Code)
            .AsNoTracking()
            .ToListAsync();
        ViewBag.PurchaseOrders = await _context.PurchaseOrders
            .Where(order => order.Status == DocumentStatus.Draft)
            .Include(order => order.Supplier)
            .Include(order => order.Items)
                .ThenInclude(item => item.Product)
            .OrderBy(order => order.OrderNo)
            .AsNoTracking()
            .ToListAsync();
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private async Task LoadIssueSelectionsAsync()
    {
        ViewBag.AvailableBalances = await _context.StockBalances
            .Include(balance => balance.Product)
            .Include(balance => balance.Lot)
            .Include(balance => balance.Location)
            .Where(balance =>
                balance.QtyAvailable > 0 &&
                balance.Product!.IsActive &&
                balance.Location!.IsActive &&
                balance.Location.Code != QcService.QuarantineLocationCode)
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
        ViewBag.SalesOrders = await _context.SalesOrders
            .Where(order => order.Status == DocumentStatus.Draft)
            .Include(order => order.Customer)
            .Include(order => order.Items)
                .ThenInclude(item => item.Product)
            .OrderBy(order => order.OrderNo)
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

        _context.ChangeTracker.Clear();
        var persistedReceipt = await _context.GoodsReceipts
            .Include(item => item.Lines)
            .FirstOrDefaultAsync(item => item.Id == receipt.Id);
        if (persistedReceipt is not null)
        {
            _context.GoodsReceipts.Remove(persistedReceipt);
            await _context.SaveChangesAsync();
        }
    }

    private async Task RollbackIssueAsync(GoodsIssue issue, IDbContextTransaction? transaction)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync();
            _context.ChangeTracker.Clear();
            return;
        }

        _context.ChangeTracker.Clear();
        var persistedIssue = await _context.GoodsIssues
            .Include(item => item.Lines)
            .FirstOrDefaultAsync(item => item.Id == issue.Id);
        if (persistedIssue is not null)
        {
            _context.GoodsIssues.Remove(persistedIssue);
            await _context.SaveChangesAsync();
        }
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

    private async Task<IReadOnlyList<LowStockLevelBefore>> CaptureLowStockLevelsAsync(
        IEnumerable<int> productIds)
    {
        var ids = productIds.Distinct().ToList();
        var products = await _context.Products
            .AsNoTracking()
            .Where(product => ids.Contains(product.Id))
            .Select(product => new
            {
                product.Id,
                product.Code,
                product.Name,
                product.MinStock
            })
            .ToListAsync();
        var levels = new List<LowStockLevelBefore>(products.Count);
        foreach (var product in products)
        {
            var availableQuantities = await _context.StockBalances
                .Where(balance => balance.ProductId == product.Id)
                .Select(balance => balance.QtyAvailable)
                .ToListAsync();
            var availableBefore = availableQuantities.Sum();
            levels.Add(new LowStockLevelBefore(
                product.Id,
                product.Code,
                product.Name,
                product.MinStock,
                availableBefore));
        }

        return levels;
    }

    private async Task<IReadOnlyList<LowStockCrossing>> CaptureLowStockCrossingsAsync(
        IEnumerable<LowStockLevelBefore> levelsBefore)
    {
        var crossings = new List<LowStockCrossing>();
        foreach (var level in levelsBefore.Where(level =>
                     level.AvailableBefore >= level.MinStock))
        {
            try
            {
                var availableQuantities = await _context.StockBalances
                    .AsNoTracking()
                    .Where(balance => balance.ProductId == level.ProductId)
                    .Select(balance => balance.QtyAvailable)
                    .ToListAsync();
                var availableAfter = availableQuantities.Sum();
                if (availableAfter >= level.MinStock)
                {
                    continue;
                }

                crossings.Add(new LowStockCrossing(
                    level.ProductId,
                    level.ProductCode,
                    level.ProductName,
                    level.MinStock,
                    availableAfter));
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Could not evaluate the low-stock crossing for product {ProductId} before committing the goods issue.",
                    level.ProductId);
            }
        }

        return crossings;
    }

    private async Task NotifyLowStockCrossingsAfterCommitAsync(
        IEnumerable<LowStockCrossing> crossings)
    {
        foreach (var crossing in crossings)
        {
            try
            {
                await _notificationService.SendNotificationAsync(
                    "Tồn kho dưới mức tối thiểu",
                    $"{crossing.ProductCode} - {crossing.ProductName}: tồn khả dụng {crossing.AvailableAfter.ToVietnameseNumber()} thấp hơn mức tối thiểu {crossing.MinStock.ToVietnameseNumber()}.",
                    "Warning",
                    "/Inventory");
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Goods issue committed but low-stock notification failed for product {ProductId}.",
                    crossing.ProductId);
            }
        }
    }

    private sealed record LowStockLevelBefore(
        int ProductId,
        string ProductCode,
        string ProductName,
        decimal MinStock,
        decimal AvailableBefore);

    private sealed record LowStockCrossing(
        int ProductId,
        string ProductCode,
        string ProductName,
        decimal MinStock,
        decimal AvailableAfter);
}
