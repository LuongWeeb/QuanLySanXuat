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
    public async Task CreatePost_RoundsAndPersistsStandardCost()
    {
        await using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"Product_Create_{Guid.NewGuid()}")
                .Options);
        context.UnitOfMeasures.Add(new UnitOfMeasure { Id = 1, Code = "EA", Name = "Each" });
        await context.SaveChangesAsync();
        var productRepository = new GenericRepository<Product>(context);
        var uomRepository = new GenericRepository<UnitOfMeasure>(context);
        var product = new Product
        {
            Code = "FG-COST",
            Name = "Finished product",
            BaseUomId = 1,
            StandardCost = 125_000.505m
        };
        var controller = new ProductController(
            new ProductService(productRepository, uomRepository),
            uomRepository,
            context)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.TempData = new TempDataDictionary(
            controller.HttpContext,
            Mock.Of<ITempDataProvider>());

        var result = await controller.Create(product);

        Assert.IsType<RedirectToActionResult>(result);
        var persisted = Assert.Single(await context.Products.ToListAsync());
        Assert.Equal(125_000.51m, persisted.StandardCost);
    }
}
