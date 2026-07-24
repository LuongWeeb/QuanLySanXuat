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

        var normalizedVersion = input.Version?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedVersion))
            ModelState.AddModelError(nameof(input.Version), "Phiên bản BOM là bắt buộc.");
        else if (normalizedVersion.Length > 50)
            ModelState.AddModelError(nameof(input.Version), "Phiên bản BOM không được vượt quá 50 ký tự.");
        else if (await _context.BOMs.AsNoTracking().AnyAsync(x =>
            x.ProductId == input.ProductId &&
            x.Version == normalizedVersion))
        {
            ModelState.AddModelError(
                nameof(input.Version),
                "Phiên bản BOM này đã tồn tại cho sản phẩm đã chọn.");
        }

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

            if (item.ComponentProductId == input.ProductId)
            {
                ModelState.AddModelError(
                    $"Items[{index}].{nameof(item.ComponentProductId)}",
                    "Sản phẩm không thể sử dụng chính nó làm vật tư thành phần.");
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
            Version = normalizedVersion!,
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

        var bom = await _context.BOMs
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id);
        if (bom is null)
        {
            if (transaction is not null)
                await transaction.RollbackAsync();
            return NotFound();
        }

        try
        {
            if (bom.IsActive)
            {
                bom.IsActive = false;
            }
            else
            {
                var activeGraphBoms = await _context.BOMs
                    .AsNoTracking()
                    .Include(x => x.Items)
                    .Where(x => x.IsActive && x.ProductId != bom.ProductId)
                    .ToListAsync();
                var dependencyGraph = activeGraphBoms
                    .GroupBy(x => x.ProductId)
                    .ToDictionary(
                        group => group.Key,
                        group => group
                            .SelectMany(x => x.Items)
                            .Select(x => x.ComponentProductId)
                            .Distinct()
                            .ToArray());
                dependencyGraph[bom.ProductId] = bom.Items
                    .Select(x => x.ComponentProductId)
                    .Distinct()
                    .ToArray();
                if (HasReachableCycle(bom.ProductId, dependencyGraph))
                {
                    if (transaction is not null)
                        await transaction.RollbackAsync();
                    _context.ChangeTracker.Clear();
                    TempData["StatusMessage"] =
                        "Không thể kích hoạt BOM vì cấu trúc vật tư tạo thành chu trình.";
                    return RedirectToAction(nameof(Index));
                }

                var activeSiblings = await _context.BOMs
                    .Where(x => x.ProductId == bom.ProductId && x.Id != bom.Id && x.IsActive)
                    .OrderBy(x => x.Id)
                    .ToListAsync();
                foreach (var sibling in activeSiblings)
                    sibling.IsActive = false;

                if (activeSiblings.Count > 0)
                    await _context.SaveChangesAsync();

                bom.IsActive = true;
            }

            await _context.SaveChangesAsync();
            if (transaction is not null)
                await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
                await transaction.RollbackAsync();
            _context.ChangeTracker.Clear();
            TempData["StatusMessage"] =
                "Không thể cập nhật trạng thái BOM do xung đột dữ liệu. Vui lòng thử lại.";
            return RedirectToAction(nameof(Index));
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync();
            _context.ChangeTracker.Clear();
            throw;
        }

        TempData["StatusMessage"] = bom.IsActive
            ? $"Đã kích hoạt BOM {bom.Version}."
            : $"Đã ngừng kích hoạt BOM {bom.Version}.";
        return RedirectToAction(nameof(Index));
    }

    private static bool HasReachableCycle(
        int rootProductId,
        IReadOnlyDictionary<int, int[]> dependencyGraph)
    {
        var visited = new HashSet<int>();
        var currentPath = new HashSet<int>();

        return Visit(rootProductId);

        bool Visit(int productId)
        {
            if (!currentPath.Add(productId))
                return true;
            if (visited.Contains(productId))
            {
                currentPath.Remove(productId);
                return false;
            }

            if (dependencyGraph.TryGetValue(productId, out var componentIds))
            {
                foreach (var componentId in componentIds)
                {
                    if (Visit(componentId))
                        return true;
                }
            }

            currentPath.Remove(productId);
            visited.Add(productId);
            return false;
        }
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
