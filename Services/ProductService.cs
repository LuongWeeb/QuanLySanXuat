using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Repositories;

namespace WmsMes.Web.Services;

public class ProductService : IProductService
{
    private readonly IGenericRepository<Product> _productRepository;
    private readonly IGenericRepository<UnitOfMeasure> _uomRepository;

    public ProductService(
        IGenericRepository<Product> productRepository,
        IGenericRepository<UnitOfMeasure> uomRepository)
    {
        _productRepository = productRepository;
        _uomRepository = uomRepository;
    }

    public async Task<IEnumerable<Product>> GetAllProductsAsync()
    {
        var products = (await _productRepository.GetAllAsync()).ToList();
        foreach (var product in products)
        {
            product.BaseUom = await _uomRepository.GetByIdAsync(product.BaseUomId);
        }

        return products;
    }

    public async Task<Product?> GetProductByIdAsync(int id)
    {
        var product = await _productRepository.GetByIdAsync(id);
        if (product is not null)
        {
            product.BaseUom = await _uomRepository.GetByIdAsync(product.BaseUomId);
        }

        return product;
    }

    public async Task<bool> CreateProductAsync(Product product)
    {
        var existingProducts = await _productRepository.GetAllAsync();
        if (existingProducts.Any(existing =>
                existing.Code.Equals(product.Code, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var uom = await _uomRepository.GetByIdAsync(product.BaseUomId);
        if (uom is null)
        {
            throw new ArgumentException("UOM does not exist.");
        }

        await _productRepository.AddAsync(product);
        await _productRepository.SaveAsync();
        return true;
    }

    public async Task UpdateProductAsync(Product product)
    {
        _productRepository.Update(product);
        await _productRepository.SaveAsync();
    }

    public async Task DeleteProductAsync(int id)
    {
        var product = await _productRepository.GetByIdAsync(id);
        if (product is null)
        {
            return;
        }

        _productRepository.Delete(product);
        await _productRepository.SaveAsync();
    }
}
