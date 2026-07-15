using Moq;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Repositories;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public class ProductServiceTests
{
    [Fact]
    public async Task CreateProductAsync_ReturnsFalse_WhenCodeAlreadyExists()
    {
        var productRepo = new Mock<IGenericRepository<Product>>();
        var uomRepo = new Mock<IGenericRepository<UnitOfMeasure>>();

        productRepo.Setup(repository => repository.GetAllAsync())
            .ReturnsAsync(new List<Product>
            {
                new() { Code = "PROD01", Name = "Existing product", BaseUomId = 1 }
            });

        var service = new ProductService(productRepo.Object, uomRepo.Object);
        var product = new Product { Code = "prod01", Name = "Duplicate product", BaseUomId = 1 };

        var result = await service.CreateProductAsync(product);

        Assert.False(result);
        uomRepo.Verify(repository => repository.GetByIdAsync(It.IsAny<int>()), Times.Never);
        productRepo.Verify(repository => repository.AddAsync(It.IsAny<Product>()), Times.Never);
        productRepo.Verify(repository => repository.SaveAsync(), Times.Never);
    }

    [Fact]
    public async Task CreateProductAsync_ThrowsArgumentException_WhenBaseUomDoesNotExist()
    {
        var productRepo = new Mock<IGenericRepository<Product>>();
        var uomRepo = new Mock<IGenericRepository<UnitOfMeasure>>();

        productRepo.Setup(repository => repository.GetAllAsync())
            .ReturnsAsync(Array.Empty<Product>());
        uomRepo.Setup(repository => repository.GetByIdAsync(42))
            .ReturnsAsync((UnitOfMeasure?)null);

        var service = new ProductService(productRepo.Object, uomRepo.Object);
        var product = new Product { Code = "PROD02", Name = "New product", BaseUomId = 42 };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateProductAsync(product));

        Assert.Equal("UOM does not exist.", exception.Message);
        productRepo.Verify(repository => repository.AddAsync(It.IsAny<Product>()), Times.Never);
        productRepo.Verify(repository => repository.SaveAsync(), Times.Never);
    }

    [Fact]
    public async Task CreateProductAsync_AddsProductAndSaves_WhenProductIsValid()
    {
        var productRepo = new Mock<IGenericRepository<Product>>();
        var uomRepo = new Mock<IGenericRepository<UnitOfMeasure>>();
        var product = new Product { Code = "PROD03", Name = "Valid product", BaseUomId = 1 };

        productRepo.Setup(repository => repository.GetAllAsync())
            .ReturnsAsync(Array.Empty<Product>());
        uomRepo.Setup(repository => repository.GetByIdAsync(1))
            .ReturnsAsync(new UnitOfMeasure { Id = 1, Code = "PCS", Name = "Pieces" });

        var service = new ProductService(productRepo.Object, uomRepo.Object);

        var result = await service.CreateProductAsync(product);

        Assert.True(result);
        productRepo.Verify(repository => repository.AddAsync(product), Times.Once);
        productRepo.Verify(repository => repository.SaveAsync(), Times.Once);
    }
}
