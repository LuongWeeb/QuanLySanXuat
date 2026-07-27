using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Repositories;
using WmsMes.Web.Services;

namespace WmsMes.Web.Controllers;

[Authorize]
public class ProductController : Controller
{
    private readonly IProductService _productService;
    private readonly IGenericRepository<UnitOfMeasure> _uomRepository;
    private readonly ApplicationDbContext _context;

    public ProductController(
        IProductService productService,
        IGenericRepository<UnitOfMeasure> uomRepository,
        ApplicationDbContext context)
    {
        _productService = productService;
        _uomRepository = uomRepository;
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        await PopulateUomsAsync();
        var products = (await _productService.GetAllProductsAsync()).ToList();
        var productIds = products.Select(product => product.Id).ToList();
        ViewData["StockBalances"] = await _context.StockBalances
            .Where(balance => productIds.Contains(balance.ProductId))
            .Include(balance => balance.Location)
            .Include(balance => balance.Lot)
            .AsNoTracking()
            .OrderBy(balance => balance.ProductId)
            .ThenBy(balance => balance.Location!.Code)
            .ThenBy(balance => balance.Lot!.LotNo)
            .ToListAsync();
        return View(products);
    }

    [Authorize(Roles = "Admin,Manager,Planner")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Product product)
    {
        product.StandardCost = Math.Round(product.StandardCost, 2, MidpointRounding.AwayFromZero);

        if (!ModelState.IsValid)
        {
            TempData["StatusMessage"] = "Dữ liệu sản phẩm chưa hợp lệ.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var created = await _productService.CreateProductAsync(product);
            TempData["StatusMessage"] = created
                ? "Đã thêm sản phẩm."
                : "Mã SKU đã tồn tại.";
        }
        catch (ArgumentException ex)
        {
            TempData["StatusMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateUomsAsync()
    {
        ViewBag.UnitOfMeasures = await _uomRepository.GetAllAsync();
    }
}
