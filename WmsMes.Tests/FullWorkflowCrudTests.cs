using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Repositories;
using WmsMes.Web.Services;
using WmsMes.Web.ViewModels;

namespace WmsMes.Tests;

public class FullWorkflowCrudTests
{
    [Fact]
    public async Task Test_FullProductCrud_AndValidation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = await CreateRelationalContextAsync(connection);

        var uom = new UnitOfMeasure { Code = "PCS", Name = "Cái" };
        context.UnitOfMeasures.Add(uom);
        await context.SaveChangesAsync();

        var repo = new GenericRepository<Product>(context);
        var uomRepo = new GenericRepository<UnitOfMeasure>(context);
        var productService = new ProductService(repo, uomRepo);
        var controller = new ProductController(productService, uomRepo, context);
        SetupControllerUser(controller, "admin-id");

        // 1. Create Product
        var newProduct = new Product
        {
            Code = "TEST-PROD-01",
            Name = "Sản phẩm thử nghiệm",
            Type = ProductType.FinishedGood,
            BaseUomId = uom.Id,
            StandardCost = 150000m,
            IsActive = true
        };

        var result = await controller.Create(newProduct);
        Assert.IsType<RedirectToActionResult>(result);

        var created = await context.Products.FirstOrDefaultAsync(p => p.Code == "TEST-PROD-01");
        Assert.NotNull(created);
        Assert.Equal("Sản phẩm thử nghiệm", created.Name);
    }

    [Fact]
    public async Task Test_SalesOrderCreationAndFulfillment_Workflow()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = await CreateRelationalContextAsync(connection);

        await SeedMasterDataAsync(context);

        var customer = new Customer { Code = "CUST-TEST", Name = "Khách hàng Test", Address = "Hà Nội" };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var productService = new SalesOrderService(context);
        var controller = new SalesOrderController(context, productService);
        SetupControllerUser(controller, "admin-id");

        var newOrder = new SalesOrder
        {
            CustomerId = customer.Id,
            DeliveryDate = DateTime.Today.AddDays(5)
        };

        var productIds = new List<int> { 1 };
        var quantities = new List<decimal> { 10m };
        var unitPrices = new List<decimal> { 250000m };

        var createResult = await controller.Create(newOrder, productIds, quantities, unitPrices);
        var redirect = Assert.IsType<RedirectToActionResult>(createResult);
        Assert.Equal("Details", redirect.ActionName);

        var createdSo = await context.SalesOrders.Include(s => s.Items).FirstOrDefaultAsync(s => s.CustomerId == customer.Id);
        Assert.NotNull(createdSo);
        Assert.Single(createdSo.Items);
        Assert.Equal(10m, createdSo.Items.First().Qty);
        Assert.Equal(DocumentStatus.Draft, createdSo.Status);
    }

    [Fact]
    public async Task Test_PurchaseOrderCreation_Workflow()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = await CreateRelationalContextAsync(connection);

        await SeedMasterDataAsync(context);

        var supplier = new Supplier { Code = "SUP-TEST", Name = "Nhà cung cấp Test" };
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        var planService = new ProductionPlanService(context);
        var prService = new PurchaseRequestService(context, planService);
        var poService = new PurchaseOrderService(context);
        var lowStockService = new LowStockService(context);
        var timeProvider = TimeProvider.System;

        var controller = new PurchaseOrderController(context, prService, poService, lowStockService, timeProvider);
        SetupControllerUser(controller, "admin-id");

        var pr = new PurchaseRequest
        {
            RequestNo = "PR-TEST-001",
            RequestDate = DateTime.UtcNow,
            RequiredDate = DateTime.UtcNow.AddDays(7),
            Status = DocumentStatus.Draft,
            Items = { new PurchaseRequestItem { ProductId = 1, Qty = 50m } }
        };
        context.PurchaseRequests.Add(pr);
        await context.SaveChangesAsync();

        var createPoResult = await controller.CreateFromRequestPost(pr.Id, supplier.Id);
        var redirect = Assert.IsType<RedirectToActionResult>(createPoResult);
        Assert.Equal("Details", redirect.ActionName);

        var createdPo = await context.PurchaseOrders.Include(p => p.Items).FirstOrDefaultAsync(p => p.SupplierId == supplier.Id);
        Assert.NotNull(createdPo);
        Assert.Single(createdPo.Items);
        Assert.Equal(50m, createdPo.Items.First().Qty);
    }

    [Fact]
    public async Task Test_WorkOrder_LifeCycle_Workflow()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = await CreateRelationalContextAsync(connection);

        await SeedMasterDataAsync(context);

        var workOrderService = new WorkOrderService(context);
        var reportExportService = new ReportExportService(context);

        var controller = new WorkOrderController(
            context,
            workOrderService,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkOrderController>.Instance,
            reportExportService,
            TimeProvider.System,
            TimeZoneInfo.Utc);
        SetupControllerUser(controller, "admin-id");

        var wo = new WorkOrder
        {
            Code = "WO-TEST-001",
            ProductId = 1,
            Qty = 100m,
            DueDate = DateTime.Today.AddDays(3),
            Status = WorkOrderStatus.InProgress,
            BomVersion = "v1.0",
            RoutingVersion = "v1.0"
        };
        context.WorkOrders.Add(wo);
        await context.SaveChangesAsync();

        var detailsResult = await controller.Details(wo.Id);
        Assert.IsType<ViewResult>(detailsResult);
    }

    private static async Task<ApplicationDbContext> CreateRelationalContextAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private static async Task SeedMasterDataAsync(ApplicationDbContext context)
    {
        var uom = new UnitOfMeasure { Code = "PCS", Name = "Cái" };
        context.UnitOfMeasures.Add(uom);
        await context.SaveChangesAsync();

        var product = new Product
        {
            Code = "PROD-01",
            Name = "Sản phẩm A",
            Type = ProductType.FinishedGood,
            BaseUomId = uom.Id,
            StandardCost = 100000m,
            IsActive = true
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var warehouse = new Warehouse { Code = "WH-01", Name = "Kho Chính" };
        context.Warehouses.Add(warehouse);
        await context.SaveChangesAsync();

        var zone = new Zone { WarehouseId = warehouse.Id, Code = "ZONE-01", Name = "Khu A" };
        context.Zones.Add(zone);
        await context.SaveChangesAsync();

        var location = new Location { ZoneId = zone.Id, Code = "LOC-01", Name = "Kệ A1" };
        context.Locations.Add(location);
        await context.SaveChangesAsync();
    }

    private static void SetupControllerUser(Controller controller, string userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, "Admin") };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var claimsPrincipal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = claimsPrincipal };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
        controller.TempData = new TempDataDictionary(httpContext, Moq.Mock.Of<ITempDataProvider>());
    }
}
