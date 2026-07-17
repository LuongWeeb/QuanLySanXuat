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
    public async Task Issues_ReturnsNewestFirstWithIssueLines()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Issues_{Guid.NewGuid()}")
            .Options;
        await using var context = new ApplicationDbContext(options);
        context.GoodsIssues.AddRange(
            new GoodsIssue { IssueNo = "GI-OLD", IssueDate = new DateTime(2026, 1, 1), Lines = { new GoodsIssueLine { Product = new Product { Code = "P-OLD", Name = "Old" }, Lot = new Lot { LotNo = "L-OLD", ProductId = 1 }, Location = new Location { Code = "A", Name = "A", Zone = new Zone { Code = "Z", Name = "Z" } }, Qty = 1 } } },
            new GoodsIssue { IssueNo = "GI-NEW", IssueDate = new DateTime(2026, 2, 1), Lines = { new GoodsIssueLine { Product = new Product { Code = "P-NEW", Name = "New" }, Lot = new Lot { LotNo = "L-NEW", ProductId = 2 }, Location = new Location { Code = "B", Name = "B", Zone = new Zone { Code = "Z2", Name = "Z2" } }, Qty = 2 } } });
        await context.SaveChangesAsync();
        var controller = new InventoryController(context);

        var result = await controller.Issues();

        var model = Assert.IsAssignableFrom<IEnumerable<GoodsIssue>>(Assert.IsType<ViewResult>(result).Model).ToList();
        Assert.Equal(new[] { "GI-NEW", "GI-OLD" }, model.Select(issue => issue.IssueNo));
        Assert.Equal("New", Assert.Single(model[0].Lines).Product?.Name);
    }

    [Fact]
    public async Task CreateIssue_Get_LoadsOnlyAvailableProductLocationLotsInFefoOrder()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Issue_Get_{Guid.NewGuid()}").Options;
        await using var context = new ApplicationDbContext(options);
        var product = new Product { Code = "P", Name = "Product", IsActive = true, ShelfLifeDays = 30 };
        var location = new Location { Code = "LOC", Name = "Location", Zone = new Zone { Code = "Z", Name = "Zone" } };
        var later = new Lot { LotNo = "LATER", Product = product, ExpiryDate = new DateTime(2026, 12, 1) };
        var sooner = new Lot { LotNo = "SOONER", Product = product, ExpiryDate = new DateTime(2026, 10, 1) };
        context.StockBalances.AddRange(
            new StockBalance { Product = product, Lot = later, Location = location, QtyAvailable = 8 },
            new StockBalance { Product = product, Lot = sooner, Location = location, QtyAvailable = 5 },
            new StockBalance { Product = product, Lot = new Lot { LotNo = "EMPTY", Product = product }, Location = location, QtyAvailable = 0 });
        await context.SaveChangesAsync();
        var controller = new InventoryController(context);

        var result = await controller.CreateIssue();

        Assert.IsType<ViewResult>(result);
        object availableBalances = controller.ViewBag.AvailableBalances;
        var balances = Assert.IsAssignableFrom<IEnumerable<StockBalance>>(availableBalances).ToList();
        Assert.Equal(new[] { "SOONER", "LATER" }, balances.Select(balance => balance.Lot!.LotNo));
    }

    [Fact]
    public async Task CreateIssue_Post_PersistsLineAndCompletesIssue()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Issue_Post_{Guid.NewGuid()}").Options;
        await using var context = new ApplicationDbContext(options);
        context.StockBalances.Add(new StockBalance { ProductId = 3, LotId = 4, LocationId = 5, QtyAvailable = 10 });
        await context.SaveChangesAsync();
        var service = new Mock<IInventoryService>();
        service.Setup(item => item.CompleteGoodsIssueAsync(It.IsAny<int>(), "system")).ReturnsAsync(true);
        var controller = new InventoryController(context, service.Object) { TempData = Mock.Of<ITempDataDictionary>() };

        var result = await controller.CreateIssue(3, 4, 2.5m, 5);

        Assert.Equal(nameof(InventoryController.Issues), Assert.IsType<RedirectToActionResult>(result).ActionName);
        var issue = await context.GoodsIssues.Include(item => item.Lines).SingleAsync();
        var line = Assert.Single(issue.Lines);
        Assert.Equal((3, 4, 2.5m, 5), (line.ProductId, line.LotId, line.Qty, line.LocationId));
        service.Verify(item => item.CompleteGoodsIssueAsync(issue.Id, "system"), Times.Once);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(0)]
    public async Task CreateIssue_Post_RejectsInvalidOrInsufficientQuantity(decimal qty)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Issue_Stock_{Guid.NewGuid()}").Options;
        await using var context = new ApplicationDbContext(options);
        context.StockBalances.Add(new StockBalance { ProductId = 3, LotId = 4, LocationId = 5, QtyAvailable = 10 });
        await context.SaveChangesAsync();
        var service = new Mock<IInventoryService>();
        var controller = new InventoryController(context, service.Object);

        var result = await controller.CreateIssue(3, 4, qty, 5);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Empty(await context.GoodsIssues.ToListAsync());
        service.Verify(item => item.CompleteGoodsIssueAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateIssue_Post_WhenCompletionFails_RemovesDraft(bool throws)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Issue_Fail_{Guid.NewGuid()}").Options;
        await using var context = new ApplicationDbContext(options);
        context.StockBalances.Add(new StockBalance { ProductId = 3, LotId = 4, LocationId = 5, QtyAvailable = 10 });
        await context.SaveChangesAsync();
        var service = new Mock<IInventoryService>();
        var setup = service.Setup(item => item.CompleteGoodsIssueAsync(It.IsAny<int>(), "system"));
        if (throws) setup.ThrowsAsync(new InvalidOperationException("failed")); else setup.ReturnsAsync(false);
        var controller = new InventoryController(context, service.Object);

        var result = await controller.CreateIssue(3, 4, 2, 5);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Empty(await context.GoodsIssues.ToListAsync());
        Assert.Empty(await context.GoodsIssueLines.ToListAsync());
    }

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

    [Fact]
    public async Task CreateReceipt_Post_WhenCompletionReturnsFalse_ShowsErrorAndRemovesDraft()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Create_False_{Guid.NewGuid()}")
            .Options;
        await using var context = new ApplicationDbContext(options);
        var inventoryService = new Mock<IInventoryService>();
        inventoryService.Setup(service => service.CompleteGoodsReceiptAsync(It.IsAny<int>(), "system"))
            .ReturnsAsync(false);
        var controller = new InventoryController(context, inventoryService.Object)
        {
            TempData = Mock.Of<ITempDataDictionary>()
        };

        var result = await controller.CreateReceipt(2, 3, "LOT-FALSE", 12.5m, 4.25m, 4);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Empty(await context.GoodsReceipts.ToListAsync());
        Assert.Empty(await context.GoodsReceiptLines.ToListAsync());
    }

    [Fact]
    public async Task CreateReceipt_Post_WhenCompletionThrows_ShowsErrorAndRemovesDraft()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Create_Exception_{Guid.NewGuid()}")
            .Options;
        await using var context = new ApplicationDbContext(options);
        var inventoryService = new Mock<IInventoryService>();
        inventoryService.Setup(service => service.CompleteGoodsReceiptAsync(It.IsAny<int>(), "system"))
            .ThrowsAsync(new InvalidOperationException("completion failed"));
        var controller = new InventoryController(context, inventoryService.Object)
        {
            TempData = Mock.Of<ITempDataDictionary>()
        };

        var result = await controller.CreateReceipt(2, 3, "LOT-ERROR", 12.5m, 4.25m, 4);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Empty(await context.GoodsReceipts.ToListAsync());
        Assert.Empty(await context.GoodsReceiptLines.ToListAsync());
    }
}
