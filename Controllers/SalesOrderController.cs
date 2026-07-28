using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Services;

namespace WmsMes.Web.Controllers;

[Authorize(Roles = "Admin,Manager,Planner")]
public class SalesOrderController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ISalesOrderService _orderService;

    public SalesOrderController(
        ApplicationDbContext context,
        ISalesOrderService orderService)
    {
        _context = context;
        _orderService = orderService;
    }

    public async Task<IActionResult> Index()
    {
        return View(await _orderService.GetAllAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var order = await _orderService.GetByIdAsync(id);
        return order is null ? NotFound() : View(order);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await LoadFormOptionsAsync();
        return View(new SalesOrder
        {
            DeliveryDate = DateTime.Today.AddDays(7)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        SalesOrder order,
        List<int> productIds,
        List<decimal> quantities,
        List<decimal> unitPrices)
    {
        order.Items.Clear();
        if (productIds.Count == 0 ||
            productIds.Count != quantities.Count ||
            productIds.Count != unitPrices.Count)
        {
            ModelState.AddModelError(
                string.Empty,
                "Đơn bán hàng phải có ít nhất một dòng sản phẩm hợp lệ.");
        }
        else
        {
            for (var index = 0; index < productIds.Count; index++)
            {
                if (quantities[index] <= 0m)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        $"Số lượng tại dòng {index + 1} phải lớn hơn 0.");
                }

                if (unitPrices[index] < 0m)
                {
                    ModelState.AddModelError(
                        string.Empty,
                        $"Đơn giá tại dòng {index + 1} không được âm.");
                }

                order.Items.Add(new SalesOrderItem
                {
                    ProductId = productIds[index],
                    Qty = quantities[index],
                    UnitPrice = unitPrices[index]
                });
            }
        }

        if (ModelState.IsValid)
        {
            var created = await _orderService.CreateAsync(
                order,
                User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system");
            if (created is not null)
            {
                TempData["StatusMessage"] = $"Đã tạo Đơn bán hàng {created.OrderNo}.";
                return RedirectToAction(nameof(Details), new { id = created.Id });
            }

            ModelState.AddModelError(
                string.Empty,
                "Không thể tạo Đơn bán hàng với dữ liệu đã nhập.");
        }

        await LoadFormOptionsAsync();
        return View(order);
    }

    private async Task LoadFormOptionsAsync()
    {
        ViewData["Customers"] = await _context.Customers
            .AsNoTracking()
            .Where(customer => customer.IsActive)
            .OrderBy(customer => customer.Code)
            .ToListAsync();
        ViewData["Products"] = await _context.Products
            .AsNoTracking()
            .Where(product => product.IsActive)
            .OrderBy(product => product.Code)
            .ToListAsync();
    }
}
