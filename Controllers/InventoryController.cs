using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;

namespace WmsMes.Web.Controllers;

[Authorize]
public class InventoryController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IInventoryService? _inventoryService;

    public InventoryController(ApplicationDbContext context, IInventoryService? inventoryService = null)
    {
        _context = context;
        _inventoryService = inventoryService;
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
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin,Warehouse,Manager")]
    public async Task<IActionResult> CreateIssue(int customerId, int productId, int lotId, decimal qty, int locationId)
    {
        var customerIsActive = await _context.Customers
            .AsNoTracking()
            .AnyAsync(customer => customer.Id == customerId && customer.IsActive);
        if (!customerIsActive)
        {
            ModelState.AddModelError(nameof(customerId), "Khách hàng không hợp lệ hoặc đã ngừng hoạt động.");
        }

        if (qty <= 0)
        {
            ModelState.AddModelError(nameof(qty), "Số lượng phải lớn hơn 0.");
        }

        var balance = await _context.StockBalances
            .Include(item => item.Product)
            .Include(item => item.Lot)
            .Include(item => item.Location)
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.ProductId == productId && item.LotId == lotId && item.LocationId == locationId);
        if (balance?.Product is null || !balance.Product.IsActive)
        {
            ModelState.AddModelError(nameof(productId), "Sản phẩm không hợp lệ hoặc đã ngừng hoạt động.");
        }

        if (balance?.Lot is null || balance.Lot.ProductId != productId)
        {
            ModelState.AddModelError(nameof(lotId), "Lô hàng không thuộc sản phẩm đã chọn.");
        }

        if (balance?.Location is null || !balance.Location.IsActive)
        {
            ModelState.AddModelError(nameof(locationId), "Vị trí không hợp lệ hoặc đã ngừng hoạt động.");
        }

        if (qty > 0 && (balance is null || balance.QtyAvailable < qty))
        {
            ModelState.AddModelError(nameof(qty), "Số lượng xuất vượt quá tồn kho khả dụng của lô tại vị trí đã chọn.");
        }

        if (!ModelState.IsValid)
        {
            await LoadIssueSelectionsAsync();
            return View();
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
            CustomerId = customerId,
            Lines =
            {
                new GoodsIssueLine { ProductId = productId, LotId = lotId, Qty = qty, LocationId = locationId }
            }
        };

        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync()
            : null;

        try
        {
            _context.GoodsIssues.Add(issue);
            await _context.SaveChangesAsync();
            if (!await _inventoryService.CompleteGoodsIssueAsync(issue.Id, "system"))
            {
                await RollbackIssueAsync(issue, transaction);
                return await IssueCompletionErrorAsync("Không thể hoàn tất phiếu xuất kho. Vui lòng thử lại.");
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync();
            }

            TempData["StatusMessage"] = $"Đã xuất kho {qty:N2} thành công.";
            return RedirectToAction(nameof(Issues));
        }
        catch (Exception)
        {
            await RollbackIssueAsync(issue, transaction);
            return await IssueCompletionErrorAsync("Có lỗi khi hoàn tất phiếu xuất kho. Vui lòng thử lại.");
        }
    }

    [HttpGet]
    [Authorize(Roles = "Admin,Warehouse,Manager")]
    public async Task<IActionResult> CreateReceipt()
    {
        await LoadReceiptSelectionsAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "Admin,Warehouse,Manager")]
    public async Task<IActionResult> CreateReceipt(
        int supplierId,
        int productId,
        string lotNo,
        decimal qty,
        decimal unitPrice,
        int locationId)
    {
        if (string.IsNullOrWhiteSpace(lotNo))
        {
            ModelState.AddModelError(nameof(lotNo), "Số lô là bắt buộc.");
        }

        if (qty <= 0)
        {
            ModelState.AddModelError(nameof(qty), "Số lượng phải lớn hơn 0.");
        }

        if (unitPrice < 0)
        {
            ModelState.AddModelError(nameof(unitPrice), "Đơn giá không được âm.");
        }

        if (!ModelState.IsValid)
        {
            await LoadReceiptSelectionsAsync();
            return View();
        }

        var receipt = new GoodsReceipt
        {
            ReceiptNo = $"GR-{DateTime.UtcNow:yyyyMMddHHmmssfff}",
            ReceiptDate = DateTime.UtcNow,
            SupplierId = supplierId,
            Status = DocumentStatus.Draft,
            Lines =
            {
                new GoodsReceiptLine
                {
                    ProductId = productId,
                    LotNo = lotNo.Trim(),
                    Qty = qty,
                    UnitPrice = unitPrice,
                    LocationId = locationId
                }
            }
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

            var completed = await _inventoryService.CompleteGoodsReceiptAsync(receipt.Id, "system");
            if (!completed)
            {
                await RollbackReceiptAsync(receipt, transaction);
                return await ReceiptCompletionErrorAsync("Không thể hoàn tất phiếu nhập kho. Vui lòng thử lại.");
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync();
            }

            TempData["StatusMessage"] = $"Đã nhập kho lô hàng {lotNo.Trim()} thành công.";
            return RedirectToAction(nameof(Receipts));
        }
        catch (Exception)
        {
            await RollbackReceiptAsync(receipt, transaction);
            return await ReceiptCompletionErrorAsync("Có lỗi khi hoàn tất phiếu nhập kho. Vui lòng thử lại.");
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

    private async Task<IActionResult> ReceiptCompletionErrorAsync(string message)
    {
        ModelState.AddModelError(string.Empty, message);
        await LoadReceiptSelectionsAsync();
        return View();
    }

    private async Task<IActionResult> IssueCompletionErrorAsync(string message)
    {
        ModelState.AddModelError(string.Empty, message);
        await LoadIssueSelectionsAsync();
        return View(nameof(CreateIssue));
    }
}
