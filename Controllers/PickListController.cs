using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Services;
using WmsMes.Web.ViewModels;

namespace WmsMes.Web.Controllers;

[Authorize(Roles = "Admin,Warehouse,Manager")]
public class PickListController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IPickListService _pickListService;

    public PickListController(
        ApplicationDbContext context,
        IPickListService pickListService)
    {
        _context = context;
        _pickListService = pickListService;
    }

    public async Task<IActionResult> Index()
    {
        var pickLists = await _context.PickLists
            .AsNoTracking()
            .Include(pickList => pickList.SalesOrder)
                .ThenInclude(salesOrder => salesOrder!.Customer)
            .OrderByDescending(pickList => pickList.CreatedAt)
            .ThenByDescending(pickList => pickList.Id)
            .ToListAsync();

        return View(pickLists);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await LoadSalesOrdersAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int salesOrderId)
    {
        if (salesOrderId <= 0)
        {
            ModelState.AddModelError(nameof(salesOrderId), "Vui lòng chọn đơn bán hàng hợp lệ.");
        }

        if (!ModelState.IsValid)
        {
            await LoadSalesOrdersAsync(salesOrderId);
            return View();
        }

        var pickList = await _pickListService.CreatePickListForSalesOrderAsync(salesOrderId);
        if (pickList is null)
        {
            ModelState.AddModelError(nameof(salesOrderId), "Đơn bán hàng không tồn tại hoặc không còn khả dụng.");
            await LoadSalesOrdersAsync(salesOrderId);
            return View();
        }

        TempData["StatusMessage"] = $"Đã tạo danh sách lấy hàng {pickList.PickListNo}.";
        return RedirectToAction(nameof(Details), new { id = pickList.Id });
    }

    public async Task<IActionResult> Details(int id)
    {
        var pickList = await _context.PickLists
            .AsNoTracking()
            .Include(list => list.SalesOrder)
                .ThenInclude(order => order!.Customer)
            .Include(list => list.Items)
                .ThenInclude(item => item.Product)
            .Include(list => list.Items)
                .ThenInclude(item => item.Location)
                    .ThenInclude(location => location!.Zone)
            .Include(list => list.Items)
                .ThenInclude(item => item.Lot)
            .SingleOrDefaultAsync(list => list.Id == id);

        return pickList is null ? NotFound() : View(pickList);
    }

    private async Task LoadSalesOrdersAsync(int? selectedId = null)
    {
        ViewData["SalesOrders"] = await _context.SalesOrders
            .AsNoTracking()
            .Where(order => order.Status != WmsMes.Web.Domain.Enums.DocumentStatus.Completed
                && order.Status != WmsMes.Web.Domain.Enums.DocumentStatus.Cancelled
                && order.Items.Any(item => item.Qty > item.DeliveredQty))
            .Select(order => new PickListSalesOrderOptionViewModel
            {
                Id = order.Id,
                OrderNo = order.OrderNo,
                CustomerName = order.Customer!.Name,
                RemainingQuantity = order.Items
                    .Where(item => item.Qty > item.DeliveredQty)
                    .Sum(item => item.Qty - item.DeliveredQty)
            })
            .OrderByDescending(order => order.Id)
            .ToListAsync();
        ViewData["SelectedSalesOrderId"] = selectedId;
    }
}
