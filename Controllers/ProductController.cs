using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Repositories;
using WmsMes.Web.Services;

namespace WmsMes.Web.Controllers;

[Authorize]
public class ProductController : Controller
{
    private readonly IProductService _productService;
    private readonly IGenericRepository<UnitOfMeasure> _uomRepository;

    public ProductController(
        IProductService productService,
        IGenericRepository<UnitOfMeasure> uomRepository)
    {
        _productService = productService;
        _uomRepository = uomRepository;
    }

    public async Task<IActionResult> Index()
    {
        await PopulateUomsAsync();
        var products = await _productService.GetAllProductsAsync();
        return View(products);
    }

    [Authorize(Roles = "Admin,Manager,Planner")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Product product)
    {
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
