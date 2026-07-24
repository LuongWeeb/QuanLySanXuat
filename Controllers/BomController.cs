using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.ViewModels;

namespace WmsMes.Web.Controllers;

[Authorize(Roles = "Admin,Planner,Manager")]
public class BomController : Controller
{
    private readonly ApplicationDbContext _context;

    public BomController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var boms = await _context.BOMs.AsNoTracking()
            .Include(x => x.Product)
            .OrderBy(x => x.Product!.Code)
            .ThenByDescending(x => x.EffectiveDate)
            .ThenBy(x => x.Version)
            .ToListAsync();

        return View(boms);
    }

    public async Task<IActionResult> Details(int id)
    {
        var bom = await _context.BOMs.AsNoTracking()
            .Include(x => x.Product)
            .Include(x => x.Items)
                .ThenInclude(x => x.ComponentProduct)
            .SingleOrDefaultAsync(x => x.Id == id);

        return bom is null ? NotFound() : View(bom);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await LoadProductChoicesAsync();
        return View(new BomCreateInputModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(BomCreateInputModel input)
    {
        if (!await _context.Products.AnyAsync(x => x.Id == input.ProductId
            && x.IsActive
            && x.IsManufactured
            && (x.Type == ProductType.FinishedGood || x.Type == ProductType.WIP)))
        {
            ModelState.AddModelError(
                nameof(input.ProductId),
                "Thành phẩm hoặc bán thành phẩm không hợp lệ hoặc đã ngừng hoạt động.");
        }

        if (string.IsNullOrWhiteSpace(input.Version))
            ModelState.AddModelError(nameof(input.Version), "Phiên bản BOM là bắt buộc.");
        else if (input.Version.Trim().Length > 50)
            ModelState.AddModelError(nameof(input.Version), "Phiên bản BOM không được vượt quá 50 ký tự.");

        if (input.EffectiveDate is null)
            ModelState.AddModelError(nameof(input.EffectiveDate), "Ngày hiệu lực là bắt buộc.");

        input.Items ??= [];
        if (input.Items.Count == 0)
        {
            ModelState.AddModelError(nameof(input.Items), "BOM phải có ít nhất một vật tư thành phần.");
            input.Items.Add(new BomItemInputModel());
        }

        var requestedComponentIds = input.Items
            .Select(x => x.ComponentProductId)
            .Distinct()
            .ToList();
        var activeComponentIds = (await _context.Products.AsNoTracking()
            .Where(x => requestedComponentIds.Contains(x.Id) && x.IsActive)
            .Select(x => x.Id)
            .ToListAsync())
            .ToHashSet();

        for (var index = 0; index < input.Items.Count; index++)
        {
            var item = input.Items[index];
            if (!activeComponentIds.Contains(item.ComponentProductId))
            {
                ModelState.AddModelError(
                    $"Items[{index}].{nameof(item.ComponentProductId)}",
                    "Vật tư thành phần không hợp lệ hoặc đã ngừng hoạt động.");
            }

            if (item.QtyPer <= 0)
            {
                ModelState.AddModelError(
                    $"Items[{index}].{nameof(item.QtyPer)}",
                    "Định mức phải lớn hơn 0.");
            }

            if (item.ScrapPercent is < 0 or > 100)
            {
                ModelState.AddModelError(
                    $"Items[{index}].{nameof(item.ScrapPercent)}",
                    "Tỷ lệ hao hụt phải từ 0 đến 100%.");
            }
        }

        if (!ModelState.IsValid)
        {
            await LoadProductChoicesAsync();
            return View(input);
        }

        var bom = new BOM
        {
            ProductId = input.ProductId,
            Version = input.Version.Trim(),
            EffectiveDate = input.EffectiveDate!.Value,
            IsActive = false,
            Items = input.Items.Select(x => new BOMItem
            {
                ComponentProductId = x.ComponentProductId,
                QtyPer = x.QtyPer,
                ScrapPercent = x.ScrapPercent
            }).ToList()
        };

        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync()
            : null;
        try
        {
            _context.BOMs.Add(bom);
            await _context.SaveChangesAsync();
            if (transaction is not null)
                await transaction.CommitAsync();
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync();
            throw;
        }

        TempData["StatusMessage"] = $"Đã tạo BOM {bom.Version} ở trạng thái chưa kích hoạt.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        await using var transaction = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable)
            : null;

        var bom = await _context.BOMs.SingleOrDefaultAsync(x => x.Id == id);
        if (bom is null)
            return NotFound();

        try
        {
            if (bom.IsActive)
            {
                bom.IsActive = false;
            }
            else
            {
                bom.IsActive = true;
                var activeSiblings = await _context.BOMs
                    .Where(x => x.ProductId == bom.ProductId && x.Id != bom.Id && x.IsActive)
                    .ToListAsync();
                foreach (var sibling in activeSiblings)
                    sibling.IsActive = false;
            }

            await _context.SaveChangesAsync();
            if (transaction is not null)
                await transaction.CommitAsync();
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync();
            throw;
        }

        TempData["StatusMessage"] = bom.IsActive
            ? $"Đã kích hoạt BOM {bom.Version}."
            : $"Đã ngừng kích hoạt BOM {bom.Version}.";
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadProductChoicesAsync()
    {
        ViewData["ParentProducts"] = await _context.Products.AsNoTracking()
            .Where(x => x.IsActive
                && x.IsManufactured
                && (x.Type == ProductType.FinishedGood || x.Type == ProductType.WIP))
            .OrderBy(x => x.Code)
            .ToListAsync();

        ViewData["ComponentProducts"] = await _context.Products.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Code)
            .ToListAsync();
    }
}
