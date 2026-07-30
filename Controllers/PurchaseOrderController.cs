using System.Security.Claims;
using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;

namespace WmsMes.Web.Controllers;

[Authorize(Roles = "Admin,Manager,Planner")]
public class PurchaseOrderController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IPurchaseRequestService _requestService;
    private readonly IPurchaseOrderService _orderService;
    private readonly TimeProvider _timeProvider;

    public PurchaseOrderController(
        ApplicationDbContext context,
        IPurchaseRequestService requestService,
        IPurchaseOrderService orderService,
        ILowStockService lowStockService,
        TimeProvider timeProvider)
    {
        _context = context;
        _requestService = requestService;
        _orderService = orderService;
        _ = lowStockService ?? throw new ArgumentNullException(nameof(lowStockService));
        _timeProvider = timeProvider;
    }

    public async Task<IActionResult> Index()
    {
        return View(await _orderService.GetAllAsync());
    }

    public async Task<IActionResult> Requests()
    {
        return View(await _requestService.GetAllAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var order = await _orderService.GetByIdAsync(id);
        return order is null ? NotFound() : View(order);
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? requestId)
    {
        await LoadSuppliersAsync();
        ViewData["Request"] = requestId.HasValue
            ? await _requestService.GetByIdAsync(requestId.Value)
            : null;
        ViewData["Requests"] = await _requestService.GetAllAsync();
        return View();
    }

    [HttpGet]
    public IActionResult CreateFromRequest(int requestId)
    {
        return RedirectToAction(nameof(Create), new { requestId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ActionName(nameof(CreateFromRequest))]
    public async Task<IActionResult> CreateFromRequestPost(
        int requestId,
        int supplierId)
    {
        var order = await _orderService.CreateOrderFromRequestAsync(
            requestId,
            supplierId,
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system");
        if (order is null)
        {
            TempData["ErrorMessage"] =
                "Không thể tạo Đơn mua hàng từ Yêu cầu mua hàng đã chọn.";
            return RedirectToAction(nameof(Create), new { requestId });
        }

        TempData["StatusMessage"] = $"Đã tạo Đơn mua hàng {order.OrderNo}.";
        return RedirectToAction(nameof(Details), new { id = order.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRequestFromLowStock()
    {
        var cancellationToken = HttpContext.RequestAborted;
        await using var transaction = _context.Database.IsRelational() &&
                                      _context.Database.CurrentTransaction is null
            ? await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;
        try
        {
            if (await HasOpenLowStockRequestAsync(cancellationToken))
            {
                TempData["StatusMessage"] =
                    "Đã có Yêu cầu mua hàng nháp đang mở từ cảnh báo tồn kho thấp.";
                return RedirectToAction(nameof(Requests));
            }

            var lowStockItems = await LowStockQuery.Create(_context)
                .ToListAsync(cancellationToken);
            var eligibleItems = lowStockItems
                .Where(item => item.SuggestedQty > 0)
                .ToList();
            if (eligibleItems.Count == 0)
            {
                TempData["ErrorMessage"] =
                    "Không có sản phẩm tồn kho thấp với số lượng đề xuất hợp lệ để tạo yêu cầu.";
                return RedirectToAction(nameof(Requests));
            }

            var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
            var request = new PurchaseRequest
            {
                RequestNo =
                    $"PR-LOWSTOCK-{utcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..38],
                RequestDate = utcNow,
                RequiredDate = utcNow.AddDays(3),
                Status = DocumentStatus.Draft,
                LowStockBatchKey = PurchaseRequest.OpenLowStockBatchKey,
                Items = eligibleItems.Select(item => new PurchaseRequestItem
                {
                    ProductId = item.ProductId,
                    Qty = item.SuggestedQty
                }).ToList()
            };

            _context.PurchaseRequests.Add(request);
            await _context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            var skippedCount = lowStockItems.Count - eligibleItems.Count;
            TempData["StatusMessage"] = skippedCount > 0
                ? $"Đã tạo Yêu cầu mua hàng {request.RequestNo}; bỏ qua {skippedCount} sản phẩm có cấu hình MaxStock không hợp lệ."
                : $"Đã tạo Yêu cầu mua hàng {request.RequestNo} từ cảnh báo tồn kho thấp.";
            return RedirectToAction(nameof(Requests));
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await transaction.DisposeAsync();
            }

            _context.ChangeTracker.Clear();
            if (await HasOpenLowStockRequestAsync(CancellationToken.None))
            {
                TempData["StatusMessage"] =
                    "Đã có Yêu cầu mua hàng nháp đang mở từ cảnh báo tồn kho thấp.";
                return RedirectToAction(nameof(Requests));
            }

            throw;
        }
    }

    private Task<bool> HasOpenLowStockRequestAsync(CancellationToken cancellationToken)
    {
        return _context.PurchaseRequests
            .AsNoTracking()
            .AnyAsync(request =>
                    request.Status == DocumentStatus.Draft &&
                    (request.LowStockBatchKey == PurchaseRequest.OpenLowStockBatchKey ||
                     request.RequestNo.StartsWith("PR-LOWSTOCK-")),
                cancellationToken);
    }

    private async Task LoadSuppliersAsync()
    {
        ViewData["Suppliers"] = await _context.Suppliers
            .AsNoTracking()
            .Where(supplier => supplier.IsActive)
            .OrderBy(supplier => supplier.Code)
            .ToListAsync();
    }
}
