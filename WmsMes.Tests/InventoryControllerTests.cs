using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Moq;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public class InventoryControllerTests
{
    [Fact]
    public async Task CreateIssue_Post_WithoutNameIdentifier_DoesNotCallService()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"IssueIdentity_{Guid.NewGuid()}").Options;
        await using var context = new ApplicationDbContext(options);
        var seeded = await SeedIssueStockAsync(context);
        var service = new Mock<IInventoryService>();
        var controller = new InventoryController(context, Mock.Of<IReportExportService>(), service.Object);

        var result = await controller.CreateIssue(seeded.customerId, seeded.productId, seeded.lotId, 1, seeded.locationId);

        Assert.IsType<ViewResult>(result);
        service.Verify(x => x.CompleteGoodsIssueWithoutNotificationAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateReceipt_Post_WhenNotificationFails_RemainsSuccessfulAfterCompletion()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"ReceiptNotify_{Guid.NewGuid()}").Options;
        await using var context = new ApplicationDbContext(options);
        context.Suppliers.Add(new Supplier { Id = 2, Code = "S", Name = "S" });
        context.Products.Add(new Product { Id = 3, Code = "P", Name = "P" });
        context.Locations.Add(new Location { Id = 4, Code = "L", Name = "L", Zone = new Zone { Code = "Z", Name = "Z" } });
        await context.SaveChangesAsync();
        var service = new Mock<IInventoryService>();
        service.Setup(x => x.CompleteGoodsReceiptWithoutNotificationAsync(It.IsAny<int>(), "warehouse-user")).ReturnsAsync(true);
        service.Setup(x => x.NotifyStockChangedAsync()).ThrowsAsync(new InvalidOperationException("hub down"));
        var controller = Authenticated(new InventoryController(context, Mock.Of<IReportExportService>(), service.Object));
        controller.TempData = Mock.Of<ITempDataDictionary>();

        var result = await controller.CreateReceipt(2, 3, "LOT", 1, 1, 4);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Single(context.GoodsReceipts);
    }

    [Fact]
    public async Task Issues_ReturnsNewestFirstWithIssueLines()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Issues_{Guid.NewGuid()}")
            .Options;
        await using var context = new ApplicationDbContext(options);
        context.GoodsIssues.AddRange(
            new GoodsIssue { IssueNo = "GI-OLD", IssueDate = new DateTime(2026, 1, 1), Customer = new Customer { Code = "C-OLD", Name = "Old Customer" }, Lines = { new GoodsIssueLine { Product = new Product { Code = "P-OLD", Name = "Old" }, Lot = new Lot { LotNo = "L-OLD", ProductId = 1 }, Location = new Location { Code = "A", Name = "A", Zone = new Zone { Code = "Z", Name = "Z" } }, Qty = 1 } } },
            new GoodsIssue { IssueNo = "GI-NEW", IssueDate = new DateTime(2026, 2, 1), Customer = new Customer { Code = "C-NEW", Name = "New Customer" }, Lines = { new GoodsIssueLine { Product = new Product { Code = "P-NEW", Name = "New" }, Lot = new Lot { LotNo = "L-NEW", ProductId = 2 }, Location = new Location { Code = "B", Name = "B", Zone = new Zone { Code = "Z2", Name = "Z2" } }, Qty = 2 } } });
        await context.SaveChangesAsync();
        var controller = new InventoryController(context, Mock.Of<IReportExportService>());

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
        var controller = new InventoryController(context, Mock.Of<IReportExportService>());

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
        var seeded = await SeedIssueStockAsync(context);
        var service = new Mock<IInventoryService>();
        service.Setup(item => item.CompleteGoodsIssueWithoutNotificationAsync(It.IsAny<int>(), "warehouse-user")).ReturnsAsync(true);
        var controller = Authenticated(new InventoryController(context, Mock.Of<IReportExportService>(), service.Object)); controller.TempData = Mock.Of<ITempDataDictionary>();

        var result = await controller.CreateIssue(seeded.customerId, seeded.productId, seeded.lotId, 2.5m, seeded.locationId);

        Assert.Equal(nameof(InventoryController.Issues), Assert.IsType<RedirectToActionResult>(result).ActionName);
        var issue = await context.GoodsIssues.Include(item => item.Lines).SingleAsync();
        Assert.Equal(seeded.customerId, issue.CustomerId);
        var line = Assert.Single(issue.Lines);
        Assert.Equal((seeded.productId, seeded.lotId, 2.5m, seeded.locationId), (line.ProductId, line.LotId, line.Qty, line.LocationId));
        service.Verify(item => item.CompleteGoodsIssueWithoutNotificationAsync(issue.Id, "warehouse-user"), Times.Once);
        service.Verify(item => item.NotifyStockChangedAsync(), Times.Once);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(0)]
    public async Task CreateIssue_Post_RejectsInvalidOrInsufficientQuantity(decimal qty)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Issue_Stock_{Guid.NewGuid()}").Options;
        await using var context = new ApplicationDbContext(options);
        var seeded = await SeedIssueStockAsync(context);
        var service = new Mock<IInventoryService>();
        var controller = Authenticated(new InventoryController(context, Mock.Of<IReportExportService>(), service.Object));

        var result = await controller.CreateIssue(seeded.customerId, seeded.productId, seeded.lotId, qty, seeded.locationId);

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
        var seeded = await SeedIssueStockAsync(context);
        var service = new Mock<IInventoryService>();
        var setup = service.Setup(item => item.CompleteGoodsIssueWithoutNotificationAsync(It.IsAny<int>(), "warehouse-user"));
        if (throws) setup.ThrowsAsync(new InvalidOperationException("failed")); else setup.ReturnsAsync(false);
        var controller = Authenticated(new InventoryController(context, Mock.Of<IReportExportService>(), service.Object));

        var result = await controller.CreateIssue(seeded.customerId, seeded.productId, seeded.lotId, 2, seeded.locationId);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Empty(await context.GoodsIssues.ToListAsync());
        Assert.Empty(await context.GoodsIssueLines.ToListAsync());
    }

    [Fact]
    public async Task CreateIssue_Get_LoadsActiveCustomersAndUsesCanonicalFefoFifoOrder()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Issue_Policy_{Guid.NewGuid()}").Options;
        await using var context = new ApplicationDbContext(options);
        context.Customers.AddRange(
            new Customer { Code = "C-A", Name = "Active", IsActive = true },
            new Customer { Code = "C-X", Name = "Inactive", IsActive = false });
        var fefo = new Product { Code = "FEFO", Name = "FEFO", ShelfLifeDays = 30 };
        var fifo = new Product { Code = "FIFO", Name = "FIFO", ShelfLifeDays = null };
        var location = new Location { Code = "L", Name = "L", Zone = new Zone { Code = "Z", Name = "Z" } };
        context.StockBalances.AddRange(
            new StockBalance { Product = fefo, Lot = new Lot { LotNo = "FEFO-LATE", Product = fefo, ExpiryDate = new DateTime(2027, 1, 1) }, Location = location, QtyAvailable = 1 },
            new StockBalance { Product = fefo, Lot = new Lot { LotNo = "FEFO-EARLY", Product = fefo, ExpiryDate = new DateTime(2026, 1, 1) }, Location = location, QtyAvailable = 1 },
            new StockBalance { Product = fifo, Lot = new Lot { Id = 20, LotNo = "FIFO-20", Product = fifo, ExpiryDate = new DateTime(2025, 1, 1) }, Location = location, QtyAvailable = 1 },
            new StockBalance { Product = fifo, Lot = new Lot { Id = 10, LotNo = "FIFO-10", Product = fifo, ExpiryDate = new DateTime(2028, 1, 1) }, Location = location, QtyAvailable = 1 });
        await context.SaveChangesAsync();
        var controller = new InventoryController(context, Mock.Of<IReportExportService>());

        await controller.CreateIssue();

        object customersObject = controller.ViewBag.Customers;
        Assert.Equal("C-A", Assert.Single(Assert.IsAssignableFrom<IEnumerable<Customer>>(customersObject)).Code);
        object balancesObject = controller.ViewBag.AvailableBalances;
        var balances = Assert.IsAssignableFrom<IEnumerable<StockBalance>>(balancesObject).ToList();
        Assert.Equal(new[] { "FEFO-EARLY", "FEFO-LATE" }, balances.Where(x => x.Product!.Code == "FEFO").Select(x => x.Lot!.LotNo));
        Assert.Equal(new[] { "FIFO-10", "FIFO-20" }, balances.Where(x => x.Product!.Code == "FIFO").Select(x => x.Lot!.LotNo));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CreateIssue_Post_RejectsInactiveProductOrLocationWithKeyedError(bool inactiveProduct, bool inactiveLocation)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Issue_Inactive_{Guid.NewGuid()}").Options;
        await using var context = new ApplicationDbContext(options);
        var product = new Product { Code = "P", Name = "P", IsActive = !inactiveProduct };
        var lot = new Lot { LotNo = "LOT", Product = product };
        var location = new Location { Code = "L", Name = "L", IsActive = !inactiveLocation, Zone = new Zone { Code = "Z", Name = "Z" } };
        context.Customers.Add(new Customer { Id = 6, Code = "C", Name = "C" });
        context.StockBalances.Add(new StockBalance { Product = product, Lot = lot, Location = location, QtyAvailable = 10 });
        await context.SaveChangesAsync();
        var controller = Authenticated(new InventoryController(context, Mock.Of<IReportExportService>(), Mock.Of<IInventoryService>()));

        var result = await controller.CreateIssue(6, product.Id, lot.Id, 1, location.Id);

        Assert.IsType<ViewResult>(result);
        var key = inactiveProduct ? "productId" : "locationId";
        Assert.True(controller.ModelState.ContainsKey(key));
        Assert.Empty(context.GoodsIssues);
    }

    [Fact]
    public async Task CreateIssue_Post_RejectsForgedLotProductTupleWithKeyedError()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Issue_Forged_{Guid.NewGuid()}").Options;
        await using var context = new ApplicationDbContext(options);
        var product = new Product { Code = "P", Name = "P" };
        var other = new Product { Code = "OTHER", Name = "Other" };
        var location = new Location { Code = "L", Name = "L", Zone = new Zone { Code = "Z", Name = "Z" } };
        var lot = new Lot { LotNo = "LOT", Product = other };
        context.Customers.Add(new Customer { Id = 6, Code = "C", Name = "C" });
        context.StockBalances.Add(new StockBalance { Product = other, Lot = lot, Location = location, QtyAvailable = 10 });
        await context.SaveChangesAsync();
        var controller = Authenticated(new InventoryController(context, Mock.Of<IReportExportService>(), Mock.Of<IInventoryService>()));

        var result = await controller.CreateIssue(6, product.Id, lot.Id, 1, location.Id);

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey("lotId"));
        Assert.Empty(context.GoodsIssues);
    }

    [Fact]
    public async Task CreateIssue_Post_WithSqlite_CommitsIssueCustomerAndStockDeductionAtomically()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var seeded = await SeedIssueStockAsync(context);
        var controller = Authenticated(new InventoryController(context, Mock.Of<IReportExportService>(), new InventoryService(context))); controller.TempData = Mock.Of<ITempDataDictionary>();

        var result = await controller.CreateIssue(seeded.customerId, seeded.productId, seeded.lotId, 3, seeded.locationId);

        Assert.IsType<RedirectToActionResult>(result);
        context.ChangeTracker.Clear();
        Assert.Equal(seeded.customerId, (await context.GoodsIssues.SingleAsync()).CustomerId);
        Assert.Equal(7, (await context.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Single(await context.StockTransactions.ToListAsync());
    }

    [Fact]
    public async Task CreateIssue_Post_WithSqlite_RollsBackDraftWhenCompletionFails()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var seeded = await SeedIssueStockAsync(context);
        var service = new Mock<IInventoryService>();
        service.Setup(x => x.CompleteGoodsIssueWithoutNotificationAsync(It.IsAny<int>(), "warehouse-user")).ReturnsAsync(false);
        var controller = Authenticated(new InventoryController(context, Mock.Of<IReportExportService>(), service.Object));

        var result = await controller.CreateIssue(seeded.customerId, seeded.productId, seeded.lotId, 3, seeded.locationId);

        Assert.IsType<ViewResult>(result);
        context.ChangeTracker.Clear();
        Assert.Empty(await context.GoodsIssues.ToListAsync());
        Assert.Equal(10, (await context.StockBalances.SingleAsync()).QtyAvailable);
    }

    [Fact]
    public async Task CompleteGoodsIssue_WithStaleRelationalContext_PreventsOversell()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        int firstIssueId;
        int secondIssueId;
        await using (var seedContext = new ApplicationDbContext(options))
        {
            await seedContext.Database.EnsureCreatedAsync();
            var seeded = await SeedIssueStockAsync(seedContext);
            var first = NewIssue(seeded, "GI-FIRST", 7);
            var second = NewIssue(seeded, "GI-SECOND", 7);
            seedContext.GoodsIssues.AddRange(first, second);
            await seedContext.SaveChangesAsync();
            firstIssueId = first.Id;
            secondIssueId = second.Id;
        }

        await using var staleContext = new ApplicationDbContext(options);
        _ = await staleContext.StockBalances.SingleAsync();
        await using (var freshContext = new ApplicationDbContext(options))
        {
            Assert.True(await new InventoryService(freshContext).CompleteGoodsIssueAsync(firstIssueId, "first"));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new InventoryService(staleContext).CompleteGoodsIssueAsync(secondIssueId, "second"));

        staleContext.ChangeTracker.Clear();
        Assert.Equal(3, (await staleContext.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Equal(WmsMes.Web.Domain.Enums.DocumentStatus.Draft, (await staleContext.GoodsIssues.FindAsync(secondIssueId))!.Status);
        Assert.Single(await staleContext.StockTransactions.ToListAsync());
    }

    private static GoodsIssue NewIssue((int customerId, int productId, int lotId, int locationId) seeded, string issueNo, decimal qty)
    {
        return new GoodsIssue
        {
            IssueNo = issueNo,
            CustomerId = seeded.customerId,
            Lines = { new GoodsIssueLine { ProductId = seeded.productId, LotId = seeded.lotId, LocationId = seeded.locationId, Qty = qty } }
        };
    }

    private static async Task<(int customerId, int productId, int lotId, int locationId)> SeedIssueStockAsync(ApplicationDbContext context)
    {
        var customer = new Customer { Code = "C", Name = "Customer" };
        var uom = new UnitOfMeasure { Code = "EA", Name = "Each" };
        var product = new Product { Code = "P", Name = "Product", BaseUom = uom };
        var lot = new Lot { LotNo = "LOT", Product = product, Qty = 10 };
        var location = new Location { Code = "L", Name = "Location", Zone = new Zone { Code = "Z", Name = "Zone", Warehouse = new Warehouse { Code = "W", Name = "Warehouse" } } };
        context.Customers.Add(customer);
        context.StockBalances.Add(new StockBalance { Product = product, Lot = lot, Location = location, QtyAvailable = 10 });
        await context.SaveChangesAsync();
        return (customer.Id, product.Id, lot.Id, location.Id);
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
            var controller = new InventoryController(context, Mock.Of<IReportExportService>());

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
        var controller = new InventoryController(context, Mock.Of<IReportExportService>());

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
        context.Suppliers.Add(new Supplier { Id = 2, Code = "SUP", Name = "Supplier" });
        context.Products.Add(new Product { Id = 3, Code = "P", Name = "Product" });
        context.Locations.Add(new Location { Id = 4, Code = "LOC", Name = "Location", Zone = new Zone { Code = "Z", Name = "Zone" } });
        await context.SaveChangesAsync();
        var inventoryService = new Mock<IInventoryService>();
        inventoryService.Setup(service => service.CompleteGoodsReceiptWithoutNotificationAsync(It.IsAny<int>(), "warehouse-user"))
            .ReturnsAsync(true);
        var controller = Authenticated(new InventoryController(context, Mock.Of<IReportExportService>(), inventoryService.Object));
        controller.TempData = Mock.Of<ITempDataDictionary>();

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
        inventoryService.Verify(service => service.CompleteGoodsReceiptWithoutNotificationAsync(receipt.Id, "warehouse-user"), Times.Once);
        inventoryService.Verify(service => service.NotifyStockChangedAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateReceipt_Post_WhenCompletionReturnsFalse_ShowsErrorAndRemovesDraft()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Create_False_{Guid.NewGuid()}")
            .Options;
        await using var context = new ApplicationDbContext(options);
        var inventoryService = new Mock<IInventoryService>();
        inventoryService.Setup(service => service.CompleteGoodsReceiptWithoutNotificationAsync(It.IsAny<int>(), "warehouse-user"))
            .ReturnsAsync(false);
        var controller = Authenticated(new InventoryController(context, Mock.Of<IReportExportService>(), inventoryService.Object));
        controller.TempData = Mock.Of<ITempDataDictionary>();

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
        inventoryService.Setup(service => service.CompleteGoodsReceiptWithoutNotificationAsync(It.IsAny<int>(), "warehouse-user"))
            .ThrowsAsync(new InvalidOperationException("completion failed"));
        var controller = Authenticated(new InventoryController(context, Mock.Of<IReportExportService>(), inventoryService.Object));
        controller.TempData = Mock.Of<ITempDataDictionary>();

        var result = await controller.CreateReceipt(2, 3, "LOT-ERROR", 12.5m, 4.25m, 4);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Empty(await context.GoodsReceipts.ToListAsync());
        Assert.Empty(await context.GoodsReceiptLines.ToListAsync());
    }
    private static T Authenticated<T>(T controller) where T : Controller
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, "warehouse-user") }, "Test"))
            }
        };
        return controller;
    }
}
