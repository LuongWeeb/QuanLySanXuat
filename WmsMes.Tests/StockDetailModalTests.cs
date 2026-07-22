using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Repositories;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public class StockDetailModalTests
{
    [Fact]
    public void ProductController_AcceptsDbContextForReadOnlyStockBalanceLookup()
    {
        Assert.Contains(typeof(ProductController).GetConstructors(), constructor =>
            constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(ApplicationDbContext)));
    }

    [Fact]
    public async Task ProductIndex_PreloadsOnlyListedProductBalancesWithLocationAndLot()
    {
        await using var context = CreateContext();
        var listedProduct = new Product { Code = "SKU-01", Name = "Sản phẩm 01" };
        var otherProduct = new Product { Code = "SKU-02", Name = "Sản phẩm 02" };
        var location = new Location { Code = "A-01", Name = "Kệ A-01", Zone = new Zone { Code = "Z-01", Name = "Khu A", Warehouse = new Warehouse { Code = "WH-01", Name = "Kho chính" } } };
        context.StockBalances.AddRange(
            new StockBalance { Product = listedProduct, Lot = new Lot { LotNo = "LOT-01", Product = listedProduct }, Location = location, QtyAvailable = 8, QtyReserved = 2, QtyOnHold = 1 },
            new StockBalance { Product = otherProduct, Lot = new Lot { LotNo = "LOT-02", Product = otherProduct }, Location = new Location { Code = "A-02", Name = "Kệ A-02", ZoneId = 1 }, QtyAvailable = 5 });
        await context.SaveChangesAsync();
        var productService = new Mock<IProductService>();
        productService.Setup(service => service.GetAllProductsAsync()).ReturnsAsync(new[] { listedProduct });
        var uomRepository = new Mock<IGenericRepository<UnitOfMeasure>>();

        var result = await new ProductController(productService.Object, uomRepository.Object, context).Index();

        var view = Assert.IsType<ViewResult>(result);
        var balances = Assert.IsAssignableFrom<IEnumerable<StockBalance>>(view.ViewData["StockBalances"]);
        var balance = Assert.Single(balances);
        Assert.Equal(("A-01", "LOT-01", 8m, 2m, 1m),
            (balance.Location!.Code, balance.Lot!.LotNo,
                balance.QtyAvailable, balance.QtyReserved, balance.QtyOnHold));
    }

    [Fact]
    public async Task WarehouseIndex_PreloadsBalancesWithProductAndLotForEachLocation()
    {
        await using var context = CreateContext();
        var product = new Product { Code = "SKU-01", Name = "Sản phẩm 01" };
        var warehouse = new Warehouse { Code = "WH-01", Name = "Kho chính" };
        var zone = new Zone { Code = "Z-01", Name = "Khu A", Warehouse = warehouse };
        var location = new Location { Code = "A-01", Name = "Kệ A-01", Zone = zone };
        var lot = new Lot { LotNo = "LOT-01", Product = product };
        context.StockBalances.Add(new StockBalance
        {
            Product = product, Lot = lot, Location = location,
            QtyAvailable = 8, QtyReserved = 2, QtyOnHold = 1
        });
        await context.SaveChangesAsync();

        var result = await new WarehouseController(context).Index();

        var view = Assert.IsType<ViewResult>(result);
        var balances = Assert.IsAssignableFrom<IEnumerable<StockBalance>>(view.ViewData["StockBalances"]);
        var balance = Assert.Single(balances);
        Assert.Equal(("A-01", "SKU-01", "LOT-01", 8m, 2m, 1m),
            (balance.Location!.Code, balance.Product!.Code, balance.Lot!.LotNo,
                balance.QtyAvailable, balance.QtyReserved, balance.QtyOnHold));
    }

    [Fact]
    public void StockDetailViews_RenderAccessibleResponsiveModalTriggersTablesAndEmptyStates()
    {
        var productView = File.ReadAllText(Path.Combine(ProjectRoot(), "Views", "Product", "Index.cshtml"));
        var warehouseView = File.ReadAllText(Path.Combine(ProjectRoot(), "Views", "Warehouse", "Index.cshtml"));

        Assert.Contains("Vị trí tồn", productView);
        Assert.Contains("data-bs-target=\"#productStockModal-@item.Id\"", productView);
        Assert.Contains("aria-labelledby=\"productStockModalLabel-@item.Id\"", productView);
        Assert.Contains("Không có tồn kho tại vị trí nào cho sản phẩm này.", productView);
        Assert.Contains("QtyReserved", productView);
        Assert.Contains("QtyOnHold", productView);
        Assert.Contains("table-responsive", productView);

        Assert.Contains("<button", warehouseView);
        Assert.Contains("location-chip", warehouseView);
        Assert.Contains("data-bs-target=\"#locationStockModal-@location.Id\"", warehouseView);
        Assert.Contains("aria-labelledby=\"locationStockModalLabel-@location.Id\"", warehouseView);
        Assert.Contains("Không có tồn kho tại vị trí này.", warehouseView);
        Assert.Contains("QtyReserved", warehouseView);
        Assert.Contains("QtyOnHold", warehouseView);
        Assert.Contains("table-responsive", warehouseView);
    }

    private static ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"StockDetail_{Guid.NewGuid()}").Options);

    private static string ProjectRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
