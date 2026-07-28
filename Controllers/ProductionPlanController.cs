using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;

namespace WmsMes.Web.Controllers;

[Authorize(Roles = "Admin,Planner,Manager")]
public class ProductionPlanController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IProductionPlanService _planService;
    private readonly IPurchaseRequestService? _purchaseRequestService;

    public ProductionPlanController(
        ApplicationDbContext context,
        IProductionPlanService planService)
        : this(context, planService, null)
    {
    }

    public ProductionPlanController(
        ApplicationDbContext context,
        IProductionPlanService planService,
        IPurchaseRequestService? purchaseRequestService)
    {
        _context = context;
        _planService = planService;
        _purchaseRequestService = purchaseRequestService;
    }

    public async Task<IActionResult> Index()
    {
        var plans = await _context.ProductionPlans
            .OrderByDescending(plan => plan.PlanDate)
            .AsNoTracking()
            .ToListAsync();
        return View(plans);
    }

    public async Task<IActionResult> Details(int id, bool runMrp = false)
    {
        var plan = await _planService.GetByIdAsync(id);
        if (plan is null)
        {
            return NotFound();
        }

        if (runMrp)
        {
            ViewData["MrpResults"] = await _planService.CalculatePlanRequirementsAsync(id);
            ViewData["MrpRun"] = true;
        }

        return View(plan);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await LoadProductsAsync();
        return View(new ProductionPlan
        {
            PlanNo = $"PP-{DateTime.UtcNow:yyyyMMddHHmmss}"
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        ProductionPlan plan,
        List<int> productIds,
        List<decimal> plannedQtys)
    {
        plan.Items.Clear();
        plan.Status = DocumentStatus.Draft;
        plan.PlanNo = plan.PlanNo?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(plan.PlanNo))
        {
            ModelState.AddModelError(nameof(plan.PlanNo), "Số kế hoạch là bắt buộc.");
        }

        if (productIds is null ||
            plannedQtys is null ||
            productIds.Count == 0 ||
            productIds.Count != plannedQtys.Count)
        {
            ModelState.AddModelError(
                string.Empty,
                "Kế hoạch sản xuất phải có ít nhất một dòng sản phẩm hợp lệ.");
        }
        else
        {
            var validProductIds = (await _context.Products
                .Where(product =>
                    productIds.Contains(product.Id) &&
                    product.IsManufactured &&
                    product.IsActive)
                .Select(product => product.Id)
                .ToListAsync())
                .ToHashSet();

            for (var index = 0; index < productIds.Count; index++)
            {
                if (!validProductIds.Contains(productIds[index]))
                {
                    ModelState.AddModelError(
                        string.Empty,
                        $"Sản phẩm tại dòng {index + 1} không hợp lệ hoặc đã ngừng hoạt động.");
                }

                if (plannedQtys[index] <= 0m)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        $"Số lượng kế hoạch tại dòng {index + 1} phải lớn hơn 0.");
                }
            }

            if (ModelState.IsValid)
            {
                for (var index = 0; index < productIds.Count; index++)
                {
                    plan.Items.Add(new ProductionPlanItem
                    {
                        ProductId = productIds[index],
                        PlannedQty = plannedQtys[index]
                    });
                }
            }
        }

        if (ModelState.IsValid)
        {
            await _planService.CreatePlanAsync(plan);
            TempData["StatusMessage"] = $"Đã tạo kế hoạch sản xuất {plan.PlanNo}.";
            return RedirectToAction(nameof(Index));
        }

        await LoadProductsAsync();
        return View(plan);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RunMrp(int id)
    {
        return RedirectToAction(nameof(Details), new { id, runMrp = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GeneratePurchaseRequest(int id)
    {
        if (_purchaseRequestService is null)
        {
            throw new InvalidOperationException(
                "Purchase request service is not configured.");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
        var request = await _purchaseRequestService.GenerateFromMrpAsync(id, userId);
        if (request is not null)
        {
            TempData["StatusMessage"] =
                $"Đã tự động tạo Yêu cầu mua hàng mã {request.RequestNo} từ kết quả MRP.";
        }
        else
        {
            TempData["StatusMessage"] =
                "Tất cả các nguyên vật liệu đều đã đủ tồn kho, không cần tạo Yêu cầu mua hàng.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateWorkOrders(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
        try
        {
            var success = await _planService.GenerateWorkOrdersAsync(id, userId);
            TempData[success ? "StatusMessage" : "ErrorMessage"] = success
                ? "Đã tự động tạo hàng loạt Lệnh sản xuất nháp thành công."
                : "Không thể tạo Lệnh sản xuất cho kế hoạch này.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(int id)
    {
        var success = await _planService.CompletePlanAsync(id);
        TempData[success ? "StatusMessage" : "ErrorMessage"] = success
            ? "Đã xác nhận kế hoạch sản xuất thành công."
            : "Không thể xác nhận kế hoạch sản xuất này.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task LoadProductsAsync()
    {
        ViewData["Products"] = await _context.Products
            .Where(product => product.IsManufactured && product.IsActive)
            .OrderBy(product => product.Code)
            .AsNoTracking()
            .ToListAsync();
    }
}
