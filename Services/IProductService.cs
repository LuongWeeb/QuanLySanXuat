using WmsMes.Web.Domain.Entities;

namespace WmsMes.Web.Services;

public interface IProductService
{
    Task<IEnumerable<Product>> GetAllProductsAsync();

    Task<Product?> GetProductByIdAsync(int id);

    Task<bool> CreateProductAsync(Product product);

    Task UpdateProductAsync(Product product);

    Task DeleteProductAsync(int id);
}
