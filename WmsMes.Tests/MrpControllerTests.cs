using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.DTOs;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public class MrpControllerTests
{
    private static DbContextOptions<ApplicationDbContext> Options(string name) =>
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options;

    [Fact]
    public async Task Index_LoadsOnlyActiveManufacturedProducts()
    {
        await using var context = new ApplicationDbContext(Options($"MRP_Get_{Guid.NewGuid()}"));
        context.Products.AddRange(Product("VALID"), Product("INACTIVE", active: false), Product("PURCHASED", manufactured: false));
        await context.SaveChangesAsync();

        var result = await Controller(context).Index();

        var view = Assert.IsType<ViewResult>(result);
        var products = Assert.IsAssignableFrom<IEnumerable<Product>>(view.ViewData["Products"]);
        Assert.Equal("VALID", Assert.Single(products).Code);
    }

    [Fact]
    public async Task Calculate_WithValidInput_PreservesResultsSelectionAndRepopulatesProducts()
    {
        await using var context = new ApplicationDbContext(Options($"MRP_Post_{Guid.NewGuid()}"));
        var selected = Product("FG-2");
        context.Products.AddRange(Product("FG-1"), selected);
        await context.SaveChangesAsync();
        var expected = new[] { new MrpResultDto { ComponentCode = "RM", ComponentName = "Material" } };
        var service = new Mock<IMrpService>();
        service.Setup(x => x.CalculateRequirementsAsync(selected.Id, 12.5m)).ReturnsAsync(expected);

        var result = await Controller(context, service.Object).Calculate(selected.Id, 12.5m);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        Assert.Same(expected, view.Model);
        Assert.Equal(selected.Id, view.ViewData["ProductId"]);
        Assert.Equal(12.5m, view.ViewData["Qty"]);
        Assert.Equal(2, Assert.IsAssignableFrom<IEnumerable<Product>>(view.ViewData["Products"]).Count());
        service.VerifyAll();
    }

    [Theory]
    [InlineData(false, true, 10)]
    [InlineData(true, false, 10)]
    [InlineData(true, true, 0)]
    public async Task Calculate_RejectsInvalidInputWithoutCallingService(bool manufactured, bool active, decimal qty)
    {
        await using var context = new ApplicationDbContext(Options($"MRP_Invalid_{Guid.NewGuid()}"));
        var product = Product("FORGED", manufactured, active);
        context.Products.AddRange(product, Product("VALID"));
        await context.SaveChangesAsync();
        var service = new Mock<IMrpService>(MockBehavior.Strict);
        var controller = Controller(context, service.Object);

        var result = await controller.Calculate(product.Id, qty);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index", view.ViewName);
        Assert.False(controller.ModelState.IsValid);
        Assert.Equal(product.Id, view.ViewData["ProductId"]);
        Assert.Equal(qty, view.ViewData["Qty"]);
        var products = Assert.IsAssignableFrom<IEnumerable<Product>>(view.ViewData["Products"]).ToList();
        Assert.Equal(manufactured && active ? 2 : 1, products.Count);
        Assert.Contains(products, p => p.Code == "VALID");
        Assert.Null(view.Model);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Calculate_WhenServiceFails_PreservesSelectionAndShowsValidationError()
    {
        await using var context = new ApplicationDbContext(Options($"MRP_Error_{Guid.NewGuid()}"));
        var product = Product("FG");
        context.Products.Add(product);
        await context.SaveChangesAsync();
        var service = new Mock<IMrpService>();
        service.Setup(x => x.CalculateRequirementsAsync(product.Id, 5m)).ThrowsAsync(new InvalidOperationException("missing BOM"));
        var controller = Controller(context, service.Object);

        var result = await controller.Calculate(product.Id, 5m);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal(product.Id, view.ViewData["ProductId"]);
        Assert.Equal(5m, view.ViewData["Qty"]);
        Assert.False(controller.ModelState.IsValid);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<Product>>(view.ViewData["Products"]));
    }

    [Fact]
    public void IndexView_UsesAccessibleManufacturedProductSelectAndValidation()
    {
        var view = File.ReadAllText(Path.Combine(ProjectRoot(), "Views", "Mrp", "Index.cshtml"));

        Assert.Contains("<label class=\"form-label\" for=\"productId\">", view);
        Assert.Contains("<select id=\"productId\" name=\"productId\"", view);
        Assert.Contains("asp-validation-summary=\"ModelOnly\"", view);
        Assert.Contains("aria-describedby=\"productId-validation\"", view);
        Assert.Contains("id=\"productId-validation\"", view);
        Assert.DoesNotContain("type=\"number\" name=\"productId\"", view);
    }

    private static MrpController Controller(ApplicationDbContext context, IMrpService? service = null) =>
        new(context, service ?? Mock.Of<IMrpService>());

    private static Product Product(string code, bool manufactured = true, bool active = true) =>
        new() { Code = code, Name = code, IsManufactured = manufactured, IsActive = active };

    private static string ProjectRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
