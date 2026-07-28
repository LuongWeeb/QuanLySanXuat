using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.ViewModels;

namespace WmsMes.Web.Controllers;

[Authorize(Roles = "Admin,QC,Manager")]
public class QcChecklistController : Controller
{
    private readonly ApplicationDbContext _context;

    public QcChecklistController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var checklists = await _context.QCChecklists
            .AsNoTracking()
            .Include(item => item.Product)
            .Include(item => item.Items)
            .OrderBy(item => item.Product!.Code)
            .ThenBy(item => item.Name)
            .ToListAsync();
        return View(checklists);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await LoadProductsAsync();
        return View(new QcChecklistInputModel
        {
            Items = { new QcChecklistItemInputModel() }
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(QcChecklistInputModel input)
    {
        await ValidateAsync(input);
        if (!ModelState.IsValid)
        {
            await LoadProductsAsync(input.ProductId);
            return View(input);
        }

        var checklist = new QCChecklist
        {
            ProductId = input.ProductId,
            Name = input.Name.Trim(),
            IsActive = input.IsActive,
            Items = CreateItems(input.Items)
        };
        _context.QCChecklists.Add(checklist);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var checklist = await _context.QCChecklists
            .AsNoTracking()
            .Include(item => item.Items)
            .SingleOrDefaultAsync(item => item.Id == id);
        if (checklist is null)
        {
            return NotFound();
        }

        await LoadProductsAsync(checklist.ProductId);
        return View(new QcChecklistInputModel
        {
            Id = checklist.Id,
            ProductId = checklist.ProductId,
            Name = checklist.Name,
            IsActive = checklist.IsActive,
            Items = checklist.Items
                .OrderBy(item => item.Id)
                .Select(item => new QcChecklistItemInputModel
                {
                    ParameterName = item.ParameterName,
                    MinVal = item.MinVal,
                    MaxVal = item.MaxVal,
                    Unit = item.Unit,
                    IsRequired = item.IsRequired
                })
                .ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(QcChecklistInputModel input)
    {
        var checklist = await _context.QCChecklists
            .Include(item => item.Items)
            .SingleOrDefaultAsync(item => item.Id == input.Id);
        if (checklist is null)
        {
            return NotFound();
        }

        await ValidateAsync(input);
        if (!ModelState.IsValid)
        {
            await LoadProductsAsync(input.ProductId);
            return View(input);
        }

        checklist.ProductId = input.ProductId;
        checklist.Name = input.Name.Trim();
        checklist.IsActive = input.IsActive;
        _context.QCChecklistItems.RemoveRange(checklist.Items);
        checklist.Items = CreateItems(input.Items);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateAsync(QcChecklistInputModel input)
    {
        if (!await _context.Products.AnyAsync(product =>
                product.Id == input.ProductId && product.IsActive))
        {
            ModelState.AddModelError(
                nameof(input.ProductId),
                "Sản phẩm không hợp lệ hoặc đã ngừng hoạt động.");
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            ModelState.AddModelError(nameof(input.Name), "Tên mẫu kiểm định là bắt buộc.");
        }

        var items = input.Items
            .Select((item, index) => (Item: item, Index: index))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Item.ParameterName))
            .ToList();
        if (items.Count == 0)
        {
            ModelState.AddModelError(nameof(input.Items), "Mẫu kiểm định phải có ít nhất một tiêu chí.");
        }

        foreach (var entry in items)
        {
            if (entry.Item.MinVal.HasValue &&
                entry.Item.MaxVal.HasValue &&
                entry.Item.MinVal > entry.Item.MaxVal)
            {
                ModelState.AddModelError(
                    $"Items[{entry.Index}].{nameof(entry.Item.MaxVal)}",
                    "Giá trị tối đa phải lớn hơn hoặc bằng giá trị tối thiểu.");
            }
        }
    }

    private static List<QCChecklistItem> CreateItems(
        IEnumerable<QcChecklistItemInputModel> inputs)
    {
        return inputs
            .Where(item => !string.IsNullOrWhiteSpace(item.ParameterName))
            .Select(item => new QCChecklistItem
            {
                ParameterName = item.ParameterName.Trim(),
                MinVal = item.MinVal,
                MaxVal = item.MaxVal,
                Unit = item.Unit?.Trim() ?? string.Empty,
                IsRequired = item.IsRequired
            })
            .ToList();
    }

    private async Task LoadProductsAsync(int? selectedId = null)
    {
        var products = await _context.Products
            .AsNoTracking()
            .Where(product => product.IsActive)
            .OrderBy(product => product.Code)
            .Select(product => new
            {
                product.Id,
                Display = product.Code + " - " + product.Name
            })
            .ToListAsync();
        ViewBag.Products = new SelectList(products, "Id", "Display", selectedId);
    }
}
