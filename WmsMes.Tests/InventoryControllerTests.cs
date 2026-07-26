using System.Data;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Moq;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Services;
using WmsMes.Web.ViewModels;

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

        var result = await controller.CreateIssue(IssueModel(seeded, 1));

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

        var result = await controller.CreateReceipt(ReceiptModel(2, 3, "LOT", 1, 1, 4));

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
            new StockBalance { Product = product, Lot = new Lot { LotNo = "EMPTY", Product = product }, Location = location, QtyAvailable = 0 },
            new StockBalance
            {
                Product = product,
                Lot = new Lot { LotNo = "QUARANTINED", Product = product },
                Location = new Location
                {
                    Code = QcService.QuarantineLocationCode,
                    Name = "Quarantine",
                    Zone = new Zone { Code = "QZ", Name = "Quarantine zone" }
                },
                QtyAvailable = 7
            });
        await context.SaveChangesAsync();
        var controller = new InventoryController(context, Mock.Of<IReportExportService>());

        var result = await controller.CreateIssue();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<CreateIssueViewModel>(view.Model);
        Assert.Single(model.Lines);
        object availableBalances = controller.ViewBag.AvailableBalances;
        var balances = Assert.IsAssignableFrom<IEnumerable<StockBalance>>(availableBalances).ToList();
        Assert.Equal(new[] { "SOONER", "LATER" }, balances.Select(balance => balance.Lot!.LotNo));
    }

    [Fact]
    public async Task CreateIssue_Post_WhenDuplicateTupleExceedsAvailability_KeysAggregateErrorToOverflowLine()
    {
        using var culture = new CultureScope("en-US");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Issue_Aggregate_{Guid.NewGuid()}").Options;
        await using var context = new ApplicationDbContext(options);
        var seeded = await SeedIssueStockAsync(context);
        var service = new Mock<IInventoryService>();
        var controller = Authenticated(new InventoryController(
            context,
            Mock.Of<IReportExportService>(),
            service.Object));
        var model = IssueModel(seeded, 6);
        model.Lines.Add(new IssueLineInput
        {
            ProductId = seeded.productId,
            LotId = seeded.lotId,
            Qty = 6,
            LocationId = seeded.locationId
        });

        var result = await controller.CreateIssue(model);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.ContainsKey("Lines[0].Qty"));
        Assert.True(controller.ModelState.ContainsKey("Lines[1].Qty"));
        var error = Assert.Single(controller.ModelState["Lines[1].Qty"]!.Errors);
        Assert.Equal(
            "Tổng số lượng yêu cầu cho cùng lô và vị trí là 12,00, vượt quá số lượng khả dụng 10,00.",
            error.ErrorMessage);
        Assert.Empty(context.GoodsIssues);
        service.Verify(item => item.CompleteGoodsIssueWithoutNotificationAsync(
            It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateIssue_Post_RejectsForgedQuarantineLocation()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Issue_Quarantine_{Guid.NewGuid()}").Options;
        await using var context = new ApplicationDbContext(options);
        var seeded = await SeedIssueStockAsync(context);
        var location = await context.Locations.FindAsync(seeded.locationId);
        location!.Code = QcService.QuarantineLocationCode;
        await context.SaveChangesAsync();
        var service = new Mock<IInventoryService>();
        var controller = Authenticated(new InventoryController(
            context,
            Mock.Of<IReportExportService>(),
            service.Object));

        var result = await controller.CreateIssue(IssueModel(seeded, 1));

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey("Lines[0].LocationId"));
        Assert.Empty(context.GoodsIssues);
        service.Verify(item => item.CompleteGoodsIssueWithoutNotificationAsync(
            It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateIssue_Post_PersistsLineAndCompletesIssue()
    {
        using var culture = new CultureScope("en-US");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Issue_Post_{Guid.NewGuid()}").Options;
        await using var context = new ApplicationDbContext(options);
        var seeded = await SeedIssueStockAsync(context);
        var service = new Mock<IInventoryService>();
        service.Setup(item => item.CompleteGoodsIssueWithoutNotificationAsync(It.IsAny<int>(), "warehouse-user")).ReturnsAsync(true);
        var controller = Authenticated(new InventoryController(context, Mock.Of<IReportExportService>(), service.Object));
        controller.TempData = new TempDataDictionary(
            controller.HttpContext,
            Mock.Of<ITempDataProvider>());

        var result = await controller.CreateIssue(IssueModel(seeded, 2.5m));

        Assert.Equal(nameof(InventoryController.Issues), Assert.IsType<RedirectToActionResult>(result).ActionName);
        var issue = await context.GoodsIssues.Include(item => item.Lines).SingleAsync();
        Assert.Equal(seeded.customerId, issue.CustomerId);
        var line = Assert.Single(issue.Lines);
        Assert.Equal((seeded.productId, seeded.lotId, 2.5m, seeded.locationId), (line.ProductId, line.LotId, line.Qty, line.LocationId));
        Assert.Equal(
            "Đã xuất kho 2,50 thành công.",
            controller.TempData["StatusMessage"]);
        service.Verify(item => item.CompleteGoodsIssueWithoutNotificationAsync(issue.Id, "warehouse-user"), Times.Once);
        service.Verify(item => item.NotifyStockChangedAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateIssue_Post_PersistsAllLinesAndCompletesOnce()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Issue_Multi_{Guid.NewGuid()}").Options;
        await using var context = new ApplicationDbContext(options);
        var seeded = await SeedIssueStockAsync(context);
        var service = new Mock<IInventoryService>();
        service.Setup(item => item.CompleteGoodsIssueWithoutNotificationAsync(It.IsAny<int>(), "warehouse-user")).ReturnsAsync(true);
        var controller = Authenticated(new InventoryController(context, Mock.Of<IReportExportService>(), service.Object));
        controller.TempData = Mock.Of<ITempDataDictionary>();
        var model = IssueModel(seeded, 2);
        model.Lines.Add(new IssueLineInput
        {
            ProductId = seeded.productId,
            LotId = seeded.lotId,
            Qty = 3,
            LocationId = seeded.locationId
        });

        var result = await controller.CreateIssue(model);

        Assert.Equal(nameof(InventoryController.Issues), Assert.IsType<RedirectToActionResult>(result).ActionName);
        var issue = await context.GoodsIssues.Include(item => item.Lines).SingleAsync();
        Assert.Equal(new[] { 2m, 3m }, issue.Lines.Select(line => line.Qty));
        service.Verify(item => item.CompleteGoodsIssueWithoutNotificationAsync(issue.Id, "warehouse-user"), Times.Once);
        service.Verify(item => item.NotifyStockChangedAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateIssue_Post_KeysValidationErrorsToTheInvalidLine()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Issue_Line_Error_{Guid.NewGuid()}").Options;
        await using var context = new ApplicationDbContext(options);
        var seeded = await SeedIssueStockAsync(context);
        var service = new Mock<IInventoryService>();
        var controller = Authenticated(new InventoryController(context, Mock.Of<IReportExportService>(), service.Object));
        var model = IssueModel(seeded, 1);
        model.Lines.Add(new IssueLineInput
        {
            ProductId = seeded.productId,
            LotId = seeded.lotId,
            Qty = 11,
            LocationId = seeded.locationId
        });

        var result = await controller.CreateIssue(model);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(model, view.Model);
        Assert.True(controller.ModelState.ContainsKey("Lines[1].Qty"));
        Assert.False(controller.ModelState.ContainsKey("Lines[0].Qty"));
        Assert.Empty(context.GoodsIssues);
        service.Verify(item => item.CompleteGoodsIssueWithoutNotificationAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateIssue_Post_ProtectsReservedStockAndReportsRemainingAvailableQuantity()
    {
        using var culture = new CultureScope("en-US");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Issue_Reserved_{Guid.NewGuid()}").Options;
        await using var context = new ApplicationDbContext(options);
        var seeded = await SeedIssueStockAsync(context);
        var balance = await context.StockBalances.SingleAsync();
        balance.QtyAvailable = 5;
        balance.QtyReserved = 10;
        await context.SaveChangesAsync();
        var service = new Mock<IInventoryService>();
        var controller = Authenticated(new InventoryController(context, Mock.Of<IReportExportService>(), service.Object));
        var model = IssueModel(seeded, 1);
        model.Lines.Add(new IssueLineInput
        {
            ProductId = seeded.productId,
            LotId = seeded.lotId,
            Qty = 6,
            LocationId = seeded.locationId
        });

        var result = await controller.CreateIssue(model);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(model, view.Model);
        var error = Assert.Single(controller.ModelState["Lines[1].Qty"]!.Errors);
        Assert.Equal(
            "Lô hàng tại vị trí đã chọn không đủ số lượng khả dụng để xuất (Chỉ còn 5,00). Số lượng giữ chỗ đang được bảo vệ.",
            error.ErrorMessage);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<StockBalance>>(controller.ViewBag.AvailableBalances));
        Assert.Empty(await context.GoodsIssues.ToListAsync());
        service.Verify(item => item.CompleteGoodsIssueWithoutNotificationAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        context.ChangeTracker.Clear();
        var unchangedBalance = await context.StockBalances.SingleAsync();
        Assert.Equal(5, unchangedBalance.QtyAvailable);
        Assert.Equal(10, unchangedBalance.QtyReserved);
    }

    [Fact]
    public async Task CreateIssue_Post_WithoutLines_RedisplaysOneBlankLine()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Issue_No_Lines_{Guid.NewGuid()}").Options;
        await using var context = new ApplicationDbContext(options);
        var seeded = await SeedIssueStockAsync(context);
        var service = new Mock<IInventoryService>();
        var controller = Authenticated(new InventoryController(context, Mock.Of<IReportExportService>(), service.Object));
        var model = new CreateIssueViewModel { CustomerId = seeded.customerId };

        var result = await controller.CreateIssue(model);

        var returnedModel = Assert.IsType<CreateIssueViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(controller.ModelState.ContainsKey("Lines"));
        Assert.Single(returnedModel.Lines);
        service.Verify(item => item.CompleteGoodsIssueWithoutNotificationAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
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

        var result = await controller.CreateIssue(IssueModel(seeded, qty));

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

        var result = await controller.CreateIssue(IssueModel(seeded, 2));

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

        var result = await controller.CreateIssue(new CreateIssueViewModel
        {
            CustomerId = 6,
            Lines = { new IssueLineInput { ProductId = product.Id, LotId = lot.Id, Qty = 1, LocationId = location.Id } }
        });

        Assert.IsType<ViewResult>(result);
        var key = inactiveProduct ? "Lines[0].ProductId" : "Lines[0].LocationId";
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

        var result = await controller.CreateIssue(new CreateIssueViewModel
        {
            CustomerId = 6,
            Lines = { new IssueLineInput { ProductId = product.Id, LotId = lot.Id, Qty = 1, LocationId = location.Id } }
        });

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey("Lines[0].LotId"));
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

        var result = await controller.CreateIssue(IssueModel(seeded, 3));

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
        IsolationLevel? observedIsolationLevel = null;
        var service = new Mock<IInventoryService>();
        service.Setup(x => x.CompleteGoodsIssueWithoutNotificationAsync(
                It.IsAny<int>(),
                "warehouse-user"))
            .Returns(async () =>
            {
                observedIsolationLevel = context.Database.CurrentTransaction!
                    .GetDbTransaction()
                    .IsolationLevel;
                await context.StockBalances.ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        balance => balance.QtyAvailable,
                        balance => balance.QtyAvailable - 3));
                return false;
            });
        var controller = Authenticated(new InventoryController(context, Mock.Of<IReportExportService>(), service.Object));

        var result = await controller.CreateIssue(IssueModel(seeded, 3));

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<CreateIssueViewModel>(view.Model);
        Assert.Single(model.Lines);
        context.ChangeTracker.Clear();
        Assert.Equal(IsolationLevel.Serializable, observedIsolationLevel);
        Assert.Empty(await context.GoodsIssues.ToListAsync());
        Assert.Equal(10, (await context.StockBalances.SingleAsync()).QtyAvailable);
    }

    [Fact]
    public async Task CreateIssue_Post_WithSqlite_RollsBackEveryLineWhenCombinedQuantityCannotComplete()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var seeded = await SeedIssueStockAsync(context);
        var controller = Authenticated(new InventoryController(context, Mock.Of<IReportExportService>(), new InventoryService(context)));
        var model = IssueModel(seeded, 6);
        model.Lines.Add(new IssueLineInput
        {
            ProductId = seeded.productId,
            LotId = seeded.lotId,
            Qty = 6,
            LocationId = seeded.locationId
        });

        var result = await controller.CreateIssue(model);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(model, view.Model);
        context.ChangeTracker.Clear();
        Assert.Empty(await context.GoodsIssues.ToListAsync());
        Assert.Empty(await context.GoodsIssueLines.ToListAsync());
        Assert.Empty(await context.StockTransactions.ToListAsync());
        Assert.Equal(10, (await context.StockBalances.SingleAsync()).QtyAvailable);
    }

    [Fact]
    public async Task CreateIssue_Post_WithInMemoryRealService_DiscardsPendingChangesWhenLaterLineFails()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Issue_InMemory_Partial_{Guid.NewGuid()}").Options;
        await using var context = new ApplicationDbContext(options);
        var seeded = await SeedIssueStockAsync(context);
        var controller = Authenticated(new InventoryController(
            context,
            Mock.Of<IReportExportService>(),
            new InventoryService(context)));
        var model = IssueModel(seeded, 6);
        model.Lines.Add(new IssueLineInput
        {
            ProductId = seeded.productId,
            LotId = seeded.lotId,
            Qty = 6,
            LocationId = seeded.locationId
        });

        var result = await controller.CreateIssue(model);

        Assert.IsType<ViewResult>(result);
        context.ChangeTracker.Clear();
        Assert.Empty(await context.GoodsIssues.ToListAsync());
        Assert.Empty(await context.GoodsIssueLines.ToListAsync());
        Assert.Empty(await context.StockTransactions.ToListAsync());
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
        context.Locations.AddRange(
            new Location { Code = "LOC", Name = "Location", Zone = new Zone { Code = "ZONE", Name = "Zone" } },
            new Location
            {
                Code = QcService.QuarantineLocationCode,
                Name = "Quarantine",
                Zone = new Zone { Code = "QZ", Name = "Quarantine zone" }
            });
        await context.SaveChangesAsync();
        var controller = new InventoryController(context, Mock.Of<IReportExportService>());

        var result = await controller.CreateReceipt();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<CreateReceiptViewModel>(view.Model);
        Assert.Single(model.Lines);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<Product>>(controller.ViewBag.Products));
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<Supplier>>(controller.ViewBag.Suppliers));
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<Location>>(controller.ViewBag.Locations));
    }

    [Fact]
    public async Task CreateReceipt_Post_RejectsDuplicateNormalizedTupleWithIndexedErrors()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Receipt_Duplicate_{Guid.NewGuid()}").Options;
        await using var context = new ApplicationDbContext(options);
        await SeedReceiptReferencesAsync(context);
        var service = new Mock<IInventoryService>();
        var controller = Authenticated(new InventoryController(
            context,
            Mock.Of<IReportExportService>(),
            service.Object));
        var model = ReceiptModel(2, 3, " Lot-1 ", 1, 2, 4);
        model.Lines.Add(new ReceiptLineInput
        {
            ProductId = 3,
            LotNo = "LOT-1",
            Qty = 2,
            UnitPrice = 3,
            LocationId = 4
        });

        var result = await controller.CreateReceipt(model);

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey("Lines[0].LotNo"));
        Assert.True(controller.ModelState.ContainsKey("Lines[1].LotNo"));
        Assert.Empty(context.GoodsReceipts);
        service.Verify(item => item.CompleteGoodsReceiptWithoutNotificationAsync(
            It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateReceipt_Post_RejectsForgedQuarantineLocation()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Receipt_Quarantine_{Guid.NewGuid()}").Options;
        await using var context = new ApplicationDbContext(options);
        await SeedReceiptReferencesAsync(context);
        var location = await context.Locations.FindAsync(4);
        location!.Code = QcService.QuarantineLocationCode;
        await context.SaveChangesAsync();
        var service = new Mock<IInventoryService>();
        var controller = Authenticated(new InventoryController(
            context,
            Mock.Of<IReportExportService>(),
            service.Object));

        var result = await controller.CreateReceipt(
            ReceiptModel(2, 3, "LOT-Q", 1, 2, 4));

        Assert.IsType<ViewResult>(result);
        Assert.True(controller.ModelState.ContainsKey("Lines[0].LocationId"));
        Assert.Empty(context.GoodsReceipts);
        service.Verify(item => item.CompleteGoodsReceiptWithoutNotificationAsync(
            It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ReceiptAndIssuePosts_ExplicitlyStartSerializableRelationalTransactions()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Controllers",
            "InventoryController.cs"));

        AssertActionUsesSerializableTransaction(source, "CreateIssue(CreateIssueViewModel model)");
        AssertActionUsesSerializableTransaction(source, "CreateReceipt(CreateReceiptViewModel model)");
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

        var result = await controller.CreateReceipt(ReceiptModel(2, 3, "LOT-9", 12.5m, 4.25m, 4));

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
    public async Task CreateReceipt_Post_PersistsAllLinesAndCompletesOnce()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Receipt_Multi_{Guid.NewGuid()}").Options;
        await using var context = new ApplicationDbContext(options);
        context.Suppliers.Add(new Supplier { Id = 2, Code = "SUP", Name = "Supplier" });
        context.Products.AddRange(
            new Product { Id = 3, Code = "P1", Name = "Product 1" },
            new Product { Id = 5, Code = "P2", Name = "Product 2" });
        context.Locations.Add(new Location { Id = 4, Code = "LOC", Name = "Location", Zone = new Zone { Code = "Z", Name = "Zone" } });
        await context.SaveChangesAsync();
        var service = new Mock<IInventoryService>();
        service.Setup(item => item.CompleteGoodsReceiptWithoutNotificationAsync(It.IsAny<int>(), "warehouse-user")).ReturnsAsync(true);
        var controller = Authenticated(new InventoryController(context, Mock.Of<IReportExportService>(), service.Object));
        controller.TempData = Mock.Of<ITempDataDictionary>();
        var model = ReceiptModel(2, 3, "LOT-1", 2, 1.25m, 4);
        model.Lines.Add(new ReceiptLineInput { ProductId = 5, LotNo = " LOT-2 ", Qty = 3, UnitPrice = 2.5m, LocationId = 4 });

        var result = await controller.CreateReceipt(model);

        Assert.Equal(nameof(InventoryController.Receipts), Assert.IsType<RedirectToActionResult>(result).ActionName);
        var receipt = await context.GoodsReceipts.Include(item => item.Lines).SingleAsync();
        Assert.Equal(new[] { "LOT-1", "LOT-2" }, receipt.Lines.Select(line => line.LotNo));
        service.Verify(item => item.CompleteGoodsReceiptWithoutNotificationAsync(receipt.Id, "warehouse-user"), Times.Once);
        service.Verify(item => item.NotifyStockChangedAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateReceipt_Post_KeysValidationErrorsToTheInvalidLine()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Receipt_Line_Error_{Guid.NewGuid()}").Options;
        await using var context = new ApplicationDbContext(options);
        context.Suppliers.Add(new Supplier { Id = 2, Code = "SUP", Name = "Supplier" });
        context.Products.Add(new Product { Id = 3, Code = "P", Name = "Product" });
        context.Locations.Add(new Location { Id = 4, Code = "LOC", Name = "Location", Zone = new Zone { Code = "Z", Name = "Zone" } });
        await context.SaveChangesAsync();
        var service = new Mock<IInventoryService>();
        var controller = Authenticated(new InventoryController(context, Mock.Of<IReportExportService>(), service.Object));
        var model = ReceiptModel(2, 3, "LOT-1", 1, 1, 4);
        model.Lines.Add(new ReceiptLineInput { ProductId = 3, LotNo = " ", Qty = 1, UnitPrice = -1, LocationId = 4 });

        var result = await controller.CreateReceipt(model);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(model, view.Model);
        Assert.True(controller.ModelState.ContainsKey("Lines[1].LotNo"));
        Assert.True(controller.ModelState.ContainsKey("Lines[1].UnitPrice"));
        Assert.False(controller.ModelState.ContainsKey("Lines[0].LotNo"));
        Assert.Empty(context.GoodsReceipts);
        service.Verify(item => item.CompleteGoodsReceiptWithoutNotificationAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateReceipt_Post_WithoutLines_RedisplaysOneBlankLine()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Receipt_No_Lines_{Guid.NewGuid()}").Options;
        await using var context = new ApplicationDbContext(options);
        context.Suppliers.Add(new Supplier { Id = 2, Code = "SUP", Name = "Supplier" });
        await context.SaveChangesAsync();
        var service = new Mock<IInventoryService>();
        var controller = Authenticated(new InventoryController(context, Mock.Of<IReportExportService>(), service.Object));
        var model = new CreateReceiptViewModel { SupplierId = 2 };

        var result = await controller.CreateReceipt(model);

        var returnedModel = Assert.IsType<CreateReceiptViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(controller.ModelState.ContainsKey("Lines"));
        Assert.Single(returnedModel.Lines);
        service.Verify(item => item.CompleteGoodsReceiptWithoutNotificationAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateReceipt_Post_WhenCompletionReturnsFalse_ShowsErrorAndRemovesDraft()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Create_False_{Guid.NewGuid()}")
            .Options;
        await using var context = new ApplicationDbContext(options);
        await SeedReceiptReferencesAsync(context);
        var inventoryService = new Mock<IInventoryService>();
        inventoryService.Setup(service => service.CompleteGoodsReceiptWithoutNotificationAsync(It.IsAny<int>(), "warehouse-user"))
            .ReturnsAsync(false);
        var controller = Authenticated(new InventoryController(context, Mock.Of<IReportExportService>(), inventoryService.Object));
        controller.TempData = Mock.Of<ITempDataDictionary>();

        var result = await controller.CreateReceipt(ReceiptModel(2, 3, "LOT-FALSE", 12.5m, 4.25m, 4));

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Empty(await context.GoodsReceipts.ToListAsync());
        Assert.Empty(await context.GoodsReceiptLines.ToListAsync());
        inventoryService.Verify(service => service.CompleteGoodsReceiptWithoutNotificationAsync(It.IsAny<int>(), "warehouse-user"), Times.Once);
    }

    [Fact]
    public async Task CreateReceipt_Post_WhenCompletionThrows_ShowsErrorAndRemovesDraft()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Inv_Create_Exception_{Guid.NewGuid()}")
            .Options;
        await using var context = new ApplicationDbContext(options);
        await SeedReceiptReferencesAsync(context);
        var inventoryService = new Mock<IInventoryService>();
        inventoryService.Setup(service => service.CompleteGoodsReceiptWithoutNotificationAsync(It.IsAny<int>(), "warehouse-user"))
            .ThrowsAsync(new InvalidOperationException("completion failed"));
        var controller = Authenticated(new InventoryController(context, Mock.Of<IReportExportService>(), inventoryService.Object));
        controller.TempData = Mock.Of<ITempDataDictionary>();

        var result = await controller.CreateReceipt(ReceiptModel(2, 3, "LOT-ERROR", 12.5m, 4.25m, 4));

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.Empty(await context.GoodsReceipts.ToListAsync());
        Assert.Empty(await context.GoodsReceiptLines.ToListAsync());
        inventoryService.Verify(service => service.CompleteGoodsReceiptWithoutNotificationAsync(It.IsAny<int>(), "warehouse-user"), Times.Once);
    }

    [Fact]
    public async Task CreateReceipt_Post_WithSqlite_UsesSerializableTransactionAndRollsBackDraft()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var uom = new UnitOfMeasure { Code = "EA", Name = "Each" };
        var product = new Product { Code = "P", Name = "Product", BaseUom = uom };
        var location = new Location
        {
            Code = "LOC",
            Name = "Location",
            Zone = new Zone
            {
                Code = "Z",
                Name = "Zone",
                Warehouse = new Warehouse { Code = "W", Name = "Warehouse" }
            }
        };
        var supplier = new Supplier { Code = "SUP", Name = "Supplier" };
        context.AddRange(product, location, supplier);
        await context.SaveChangesAsync();
        IsolationLevel? observedIsolationLevel = null;
        var service = new Mock<IInventoryService>();
        service.Setup(item => item.CompleteGoodsReceiptWithoutNotificationAsync(
                It.IsAny<int>(),
                "warehouse-user"))
            .Returns(() =>
            {
                observedIsolationLevel = context.Database.CurrentTransaction!
                    .GetDbTransaction()
                    .IsolationLevel;
                return Task.FromResult(false);
            });
        var controller = Authenticated(new InventoryController(
            context,
            Mock.Of<IReportExportService>(),
            service.Object));

        var result = await controller.CreateReceipt(ReceiptModel(
            supplier.Id,
            product.Id,
            "LOT-ROLLBACK",
            2,
            1,
            location.Id));

        Assert.IsType<ViewResult>(result);
        Assert.Equal(IsolationLevel.Serializable, observedIsolationLevel);
        context.ChangeTracker.Clear();
        Assert.Empty(await context.GoodsReceipts.ToListAsync());
        Assert.Empty(await context.GoodsReceiptLines.ToListAsync());
        Assert.Empty(await context.Lots.ToListAsync());
        Assert.Empty(await context.StockBalances.ToListAsync());
    }
    [Theory]
    [InlineData(nameof(InventoryController.CancelReceipt))]
    [InlineData(nameof(InventoryController.CancelIssue))]
    public void CancellationPosts_RequireWarehouseAuthorizationAndAntiforgery(string actionName)
    {
        var action = typeof(InventoryController).GetMethod(actionName, new[] { typeof(int) });

        Assert.NotNull(action);
        Assert.Single(action!.GetCustomAttributes(typeof(HttpPostAttribute), true));
        Assert.Single(action.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true));
        Assert.Equal(
            "Admin,Warehouse,Manager",
            Assert.Single(action.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>()).Roles);
    }

    [Fact]
    public async Task CancelReceipt_WhenServiceSucceeds_UsesAuthenticatedUserAndRedirectsWithSuccess()
    {
        await using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"CancelReceiptController_{Guid.NewGuid()}")
                .Options);
        var service = new Mock<IInventoryService>();
        service.Setup(item => item.CancelGoodsReceiptAsync(42, "warehouse-user"))
            .ReturnsAsync(true);
        var controller = CancellationController(context, service.Object, authenticated: true);

        var result = await controller.CancelReceipt(42);

        Assert.Equal(
            nameof(InventoryController.Receipts),
            Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal(
            "Đã hủy phiếu nhập kho và hoàn trả số dư thành công.",
            controller.TempData["StatusMessage"]);
        Assert.False(controller.TempData.ContainsKey("ErrorMessage"));
        service.Verify(item => item.CancelGoodsReceiptAsync(42, "warehouse-user"), Times.Once);
    }

    [Fact]
    public async Task CancelIssue_WhenServiceReturnsFalse_UsesSystemFallbackAndRedirectsWithError()
    {
        await using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"CancelIssueController_{Guid.NewGuid()}")
                .Options);
        var service = new Mock<IInventoryService>();
        service.Setup(item => item.CancelGoodsIssueAsync(17, "system"))
            .ReturnsAsync(false);
        var controller = CancellationController(context, service.Object);

        var result = await controller.CancelIssue(17);

        Assert.Equal(
            nameof(InventoryController.Issues),
            Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal(
            "Không thể hủy phiếu xuất kho.",
            controller.TempData["ErrorMessage"]);
        Assert.False(controller.TempData.ContainsKey("StatusMessage"));
        service.Verify(item => item.CancelGoodsIssueAsync(17, "system"), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationPost_WhenServiceThrows_DoesNotExposeExceptionDetails(bool receipt)
    {
        await using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"CancelFailureController_{Guid.NewGuid()}")
                .Options);
        var service = new Mock<IInventoryService>();
        if (receipt)
        {
            service.Setup(item => item.CancelGoodsReceiptAsync(9, It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("secret database detail"));
        }
        else
        {
            service.Setup(item => item.CancelGoodsIssueAsync(9, It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("secret database detail"));
        }
        var controller = CancellationController(context, service.Object, authenticated: true);

        var result = receipt
            ? await controller.CancelReceipt(9)
            : await controller.CancelIssue(9);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(
            receipt ? nameof(InventoryController.Receipts) : nameof(InventoryController.Issues),
            redirect.ActionName);
        var message = Assert.IsType<string>(controller.TempData["ErrorMessage"]);
        Assert.DoesNotContain("secret", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Vui lòng thử lại", message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationPost_WithoutInventoryService_FailsFast(bool receipt)
    {
        await using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"CancelMissingService_{Guid.NewGuid()}")
                .Options);
        var controller = CancellationController(context);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            receipt ? controller.CancelReceipt(1) : controller.CancelIssue(1));

        Assert.Equal("IInventoryService is required.", exception.Message);
    }

    [Fact]
    public async Task Transactions_ReturnsNewestFirstWithDisplayRelationships()
    {
        await using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"InventoryTransactions_{Guid.NewGuid()}")
                .Options);
        var product = new Product { Code = "P", Name = "Product" };
        var lot = new Lot { LotNo = "LOT", Product = product };
        var location = new Location
        {
            Code = "LOC",
            Name = "Location",
            Zone = new Zone { Code = "Z", Name = "Zone" }
        };
        context.StockTransactions.AddRange(
            new StockTransaction
            {
                Type = WmsMes.Web.Domain.Enums.TransactionType.Receipt,
                Product = product,
                Lot = lot,
                Location = location,
                Qty = 3,
                TransactionDate = new DateTime(2026, 1, 1),
                UserId = "old",
                ReferenceNo = "OLD"
            },
            new StockTransaction
            {
                Type = WmsMes.Web.Domain.Enums.TransactionType.Issue,
                Product = product,
                Lot = lot,
                Location = location,
                Qty = -1,
                TransactionDate = new DateTime(2026, 2, 1),
                UserId = "new",
                ReferenceNo = "NEW"
            });
        await context.SaveChangesAsync();
        var controller = new InventoryController(context, Mock.Of<IReportExportService>());

        var result = await controller.Transactions();

        var model = Assert.IsAssignableFrom<IEnumerable<StockTransaction>>(
            Assert.IsType<ViewResult>(result).Model).ToList();
        Assert.Equal(new[] { "NEW", "OLD" }, model.Select(item => item.ReferenceNo));
        Assert.All(model, item =>
        {
            Assert.NotNull(item.Product);
            Assert.NotNull(item.Lot);
            Assert.NotNull(item.Location);
        });
    }

    private static InventoryController CancellationController(
        ApplicationDbContext context,
        IInventoryService? inventoryService = null,
        bool authenticated = false)
    {
        var controller = new InventoryController(
            context,
            Mock.Of<IReportExportService>(),
            inventoryService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        if (authenticated)
        {
            controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "warehouse-user") },
                "Test"));
        }
        controller.TempData = new TempDataDictionary(
            controller.HttpContext,
            Mock.Of<ITempDataProvider>());
        return controller;
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

    private static CreateIssueViewModel IssueModel(
        (int customerId, int productId, int lotId, int locationId) seeded,
        decimal qty) => new()
        {
            CustomerId = seeded.customerId,
            Lines =
            {
                new IssueLineInput
                {
                    ProductId = seeded.productId,
                    LotId = seeded.lotId,
                    Qty = qty,
                    LocationId = seeded.locationId
                }
            }
        };

    private static CreateReceiptViewModel ReceiptModel(
        int supplierId,
        int productId,
        string lotNo,
        decimal qty,
        decimal unitPrice,
        int locationId) => new()
        {
            SupplierId = supplierId,
            Lines =
            {
                new ReceiptLineInput
                {
                    ProductId = productId,
                    LotNo = lotNo,
                    Qty = qty,
                    UnitPrice = unitPrice,
                    LocationId = locationId
                }
            }
        };

    private static async Task SeedReceiptReferencesAsync(ApplicationDbContext context)
    {
        context.Suppliers.Add(new Supplier { Id = 2, Code = "SUP", Name = "Supplier" });
        context.Products.Add(new Product { Id = 3, Code = "P", Name = "Product" });
        context.Locations.Add(new Location
        {
            Id = 4,
            Code = "LOC",
            Name = "Location",
            Zone = new Zone { Code = "Z", Name = "Zone" }
        });
        await context.SaveChangesAsync();
    }

    private static void AssertActionUsesSerializableTransaction(string source, string signature)
    {
        var actionStart = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(actionStart >= 0, $"Could not find action {signature}.");
        var nextAction = source.IndexOf("\n    [Http", actionStart + signature.Length, StringComparison.Ordinal);
        var actionSource = nextAction >= 0
            ? source[actionStart..nextAction]
            : source[actionStart..];

        Assert.Contains(
            "BeginTransactionAsync(IsolationLevel.Serializable)",
            actionSource,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WmsMes.sln")))
            directory = directory.Parent;

        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

        public CultureScope(string cultureName)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _originalCulture;
            CultureInfo.CurrentUICulture = _originalUiCulture;
        }
    }
}
