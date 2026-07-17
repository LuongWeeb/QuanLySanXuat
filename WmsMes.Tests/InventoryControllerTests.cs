using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public class InventoryControllerTests
{
    [Fact]
    public async Task Receipts_ReturnsViewWithReceiptLines()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Receipts_{Guid.NewGuid()}")
            .Options;

        await using (var context = new ApplicationDbContext(options))
        {
            context.GoodsReceipts.Add(new GoodsReceipt
            {
                ReceiptNo = "GR-1",
                Supplier = new Supplier { Code = "SUP-1", Name = "Supplier 1" },
                Lines =
                {
                    new GoodsReceiptLine
                    {
                        Product = new Product { Code = "P-1", Name = "Product 1" },
                        LotNo = "LOT-1",
                        Qty = 50,
                        Location = new Location { Code = "LOC-1", Name = "Location 1", Zone = new Zone { Code = "Z-1", Name = "Zone 1" } }
                    }
                }
            });
            await context.SaveChangesAsync();
        }

        await using (var context = new ApplicationDbContext(options))
        {
            var controller = new InventoryController(context);

            var result = await controller.Receipts();

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsAssignableFrom<IEnumerable<GoodsReceipt>>(viewResult.Model);
            var receipt = Assert.Single(model);
            Assert.Equal("Product 1", Assert.Single(receipt.Lines).Product?.Name);
        }
    }

    [Fact]
    public async Task CreateReceipt_Get_LoadsActiveSelectionLists()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Create_Get_{Guid.NewGuid()}")
            .Options;
        await using var context = new ApplicationDbContext(options);
        context.Products.AddRange(
            new Product { Code = "ACTIVE", Name = "Active", IsActive = true },
            new Product { Code = "INACTIVE", Name = "Inactive", IsActive = false });
        context.Suppliers.Add(new Supplier { Code = "SUP", Name = "Supplier" });
        context.Locations.Add(new Location { Code = "LOC", Name = "Location", Zone = new Zone { Code = "ZONE", Name = "Zone" } });
        await context.SaveChangesAsync();
        var controller = new InventoryController(context);

        var result = await controller.CreateReceipt();

        Assert.IsType<ViewResult>(result);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<Product>>(controller.ViewBag.Products));
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<Supplier>>(controller.ViewBag.Suppliers));
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<Location>>(controller.ViewBag.Locations));
    }

    [Fact]
    public async Task CreateReceipt_Post_PersistsLineAndCompletesReceipt()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Create_Post_{Guid.NewGuid()}")
            .Options;
        await using var context = new ApplicationDbContext(options);
        var inventoryService = new Mock<IInventoryService>();
        inventoryService.Setup(service => service.CompleteGoodsReceiptAsync(It.IsAny<int>(), "system"))
            .ReturnsAsync(true);
        var controller = new InventoryController(context, inventoryService.Object)
        {
            TempData = Mock.Of<ITempDataDictionary>()
        };

        var result = await controller.CreateReceipt(2, 3, "LOT-9", 12.5m, 4.25m, 4);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(InventoryController.Receipts), redirect.ActionName);
        var receipt = await context.GoodsReceipts.Include(item => item.Lines).SingleAsync();
        var line = Assert.Single(receipt.Lines);
        Assert.Equal(2, receipt.SupplierId);
        Assert.Equal(3, line.ProductId);
        Assert.Equal("LOT-9", line.LotNo);
        Assert.Equal(12.5m, line.Qty);
        Assert.Equal(4.25m, line.UnitPrice);
        Assert.Equal(4, line.LocationId);
        inventoryService.Verify(service => service.CompleteGoodsReceiptAsync(receipt.Id, "system"), Times.Once);
    }
}
