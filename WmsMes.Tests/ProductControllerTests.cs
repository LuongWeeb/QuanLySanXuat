using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Repositories;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public class ProductControllerTests
{
    [Fact]
    public async Task CreatePost_RoundsAndPassesStandardCostToProductServiceForPersistence()
    {
        await using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"Product_Create_{Guid.NewGuid()}")
                .Options);
        var productService = new Mock<IProductService>();
        var product = new Product
        {
            Code = "FG-COST",
            Name = "Finished product",
            BaseUomId = 1,
            StandardCost = 125_000.505m
        };
        productService.Setup(service => service.CreateProductAsync(
                It.Is<Product>(candidate => candidate.StandardCost == 125_000.51m)))
            .ReturnsAsync(true);
        var controller = new ProductController(
            productService.Object,
            Mock.Of<IGenericRepository<UnitOfMeasure>>(),
            context)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.TempData = new TempDataDictionary(
            controller.HttpContext,
            Mock.Of<ITempDataProvider>());

        var result = await controller.Create(product);

        Assert.IsType<RedirectToActionResult>(result);
        productService.Verify(service => service.CreateProductAsync(
            It.Is<Product>(candidate => candidate.StandardCost == 125_000.51m)), Times.Once);
    }
}
