using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.DTOs;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public class BuyingSellingIntegrationTests
{
    [Fact]
    public void InventoryVoucherInputs_ExposePurchaseAndSalesOrderLinks()
    {
        Assert.NotNull(typeof(WmsMes.Web.ViewModels.CreateReceiptViewModel)
            .GetProperty("PurchaseOrderId"));
        Assert.NotNull(typeof(WmsMes.Web.ViewModels.CreateIssueViewModel)
            .GetProperty("SalesOrderId"));
    }

    [Fact]
    public async Task GenerateFromMrpAsync_CreatesPurchaseRequestForNetDemandOnly()
    {
        await using var context = CreateInMemoryContext();
        context.ProductionPlans.Add(new ProductionPlan
        {
            Id = 1,
            PlanNo = "PP-20260728-001"
        });
        await context.SaveChangesAsync();
        var planService = new Mock<IProductionPlanService>();
        planService
            .Setup(service => service.CalculatePlanRequirementsAsync(1))
            .ReturnsAsync(
            [
                new MrpResultDto { ComponentProductId = 10, NetDemand = 7.5m },
                new MrpResultDto { ComponentProductId = 20, NetDemand = 0m }
            ]);

        var service = CreateService(
            "WmsMes.Web.Services.PurchaseRequestService",
            context,
            planService.Object);
        var request = await InvokeAsync<PurchaseRequest?>(
            service,
            "GenerateFromMrpAsync",
            1,
            "planner");

        Assert.NotNull(request);
        Assert.Equal("PR-PP-20260728-001", request.RequestNo);
        Assert.Equal(DocumentStatus.Draft, request.Status);
        var item = Assert.Single(request.Items);
        Assert.Equal(10, item.ProductId);
        Assert.Equal(7.5m, item.Qty);
        Assert.Equal(1, await context.PurchaseRequests.CountAsync());
    }

    [Fact]
    public async Task CreateOrderFromRequestAsync_CopiesItemsAtStandardCost()
    {
        await using var context = CreateInMemoryContext();
        await SeedPurchasingMasterDataAsync(context);
        context.PurchaseRequests.Add(new PurchaseRequest
        {
            Id = 1,
            RequestNo = "PR-PP-001",
            RequiredDate = new DateTime(2026, 8, 5),
            Items =
            {
                new PurchaseRequestItem { ProductId = 1, Qty = 12m }
            }
        });
        await context.SaveChangesAsync();

        var service = CreateService(
            "WmsMes.Web.Services.PurchaseOrderService",
            context);
        var order = await InvokeAsync<PurchaseOrder?>(
            service,
            "CreateOrderFromRequestAsync",
            1,
            1,
            "buyer");

        Assert.NotNull(order);
        Assert.Matches("^PO-[0-9]{8}-[0-9]{3}$", order.OrderNo);
        Assert.Equal(1, order.SupplierId);
        Assert.Equal(1, order.PurchaseRequestId);
        var item = Assert.Single(order.Items);
        Assert.Equal(12m, item.Qty);
        Assert.Equal(42.25m, item.UnitPrice);
        Assert.Equal(DocumentStatus.Completed, (await context.PurchaseRequests.FindAsync(1))!.Status);
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_AccumulatesReceivedQuantityAndCompletesPurchaseOrder()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = await CreateRelationalContextAsync(connection);
        await SeedInventoryMasterDataAsync(context);
        context.Suppliers.Add(new Supplier { Id = 1, Code = "SUP", Name = "Supplier" });
        context.PurchaseOrders.Add(new PurchaseOrder
        {
            Id = 1,
            OrderNo = "PO-20260728-001",
            SupplierId = 1,
            ExpectedDeliveryDate = DateTime.UtcNow.AddDays(1),
            Items =
            {
                new PurchaseOrderItem { ProductId = 1, Qty = 5m, ReceivedQty = 2m }
            }
        });
        context.GoodsReceipts.Add(new GoodsReceipt
        {
            Id = 1,
            ReceiptNo = "GR-PO-001",
            PurchaseOrderId = 1,
            Lines =
            {
                new GoodsReceiptLine
                {
                    ProductId = 1,
                    LocationId = 1,
                    LotNo = "LOT-PO-001",
                    Qty = 3m,
                    UnitPrice = 42.25m
                }
            }
        });
        await context.SaveChangesAsync();

        var completed = await new InventoryService(context)
            .CompleteGoodsReceiptAsync(1, "warehouse");

        Assert.True(completed);
        context.ChangeTracker.Clear();
        var order = await context.PurchaseOrders.Include(candidate => candidate.Items).SingleAsync();
        Assert.Equal(5m, Assert.Single(order.Items).ReceivedQty);
        Assert.Equal(DocumentStatus.Completed, order.Status);

        Assert.True(await new InventoryService(context)
            .CancelGoodsReceiptAsync(1, "warehouse"));
        context.ChangeTracker.Clear();
        order = await context.PurchaseOrders.Include(candidate => candidate.Items).SingleAsync();
        Assert.Equal(2m, Assert.Single(order.Items).ReceivedQty);
        Assert.Equal(DocumentStatus.Draft, order.Status);
    }

    [Fact]
    public async Task CompleteGoodsIssueAsync_AccumulatesDeliveredQuantityAndCompletesSalesOrder()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = await CreateRelationalContextAsync(connection);
        await SeedInventoryMasterDataAsync(context);
        context.Customers.Add(new Customer { Id = 1, Code = "CUS", Name = "Customer" });
        context.Lots.Add(new Lot
        {
            Id = 1,
            ProductId = 1,
            LotNo = "LOT-SO-001",
            Qty = 10m,
            UnitPrice = 42.25m
        });
        context.StockBalances.Add(new StockBalance
        {
            ProductId = 1,
            LotId = 1,
            LocationId = 1,
            QtyAvailable = 10m
        });
        context.SalesOrders.Add(new SalesOrder
        {
            Id = 1,
            OrderNo = "SO-20260728-001",
            CustomerId = 1,
            DeliveryDate = DateTime.UtcNow.AddDays(1),
            Items =
            {
                new SalesOrderItem { ProductId = 1, Qty = 5m, DeliveredQty = 2m }
            }
        });
        context.GoodsIssues.Add(new GoodsIssue
        {
            Id = 1,
            IssueNo = "GI-SO-001",
            CustomerId = 1,
            SalesOrderId = 1,
            Lines =
            {
                new GoodsIssueLine
                {
                    ProductId = 1,
                    LotId = 1,
                    LocationId = 1,
                    Qty = 3m
                }
            }
        });
        await context.SaveChangesAsync();

        var completed = await new InventoryService(context)
            .CompleteGoodsIssueAsync(1, "warehouse");

        Assert.True(completed);
        context.ChangeTracker.Clear();
        var order = await context.SalesOrders.Include(candidate => candidate.Items).SingleAsync();
        Assert.Equal(5m, Assert.Single(order.Items).DeliveredQty);
        Assert.Equal(DocumentStatus.Completed, order.Status);

        Assert.True(await new InventoryService(context)
            .CancelGoodsIssueAsync(1, "warehouse"));
        context.ChangeTracker.Clear();
        order = await context.SalesOrders.Include(candidate => candidate.Items).SingleAsync();
        Assert.Equal(2m, Assert.Single(order.Items).DeliveredQty);
        Assert.Equal(DocumentStatus.Draft, order.Status);
    }

    private static object CreateService(string typeName, params object[] arguments)
    {
        var serviceType = typeof(ApplicationDbContext).Assembly.GetType(typeName);
        Assert.NotNull(serviceType);
        return Activator.CreateInstance(serviceType!, arguments)!;
    }

    private static async Task<T> InvokeAsync<T>(
        object target,
        string methodName,
        params object[] arguments)
    {
        var method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task<T>>(method!.Invoke(target, arguments));
        return await task;
    }

    private static ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<ApplicationDbContext> CreateRelationalContextAsync(
        SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private static async Task SeedPurchasingMasterDataAsync(ApplicationDbContext context)
    {
        context.UnitOfMeasures.Add(new UnitOfMeasure { Id = 1, Code = "PCS", Name = "Pieces" });
        context.Products.Add(new Product
        {
            Id = 1,
            Code = "RAW-01",
            Name = "Raw material",
            BaseUomId = 1,
            StandardCost = 42.25m
        });
        context.Suppliers.Add(new Supplier { Id = 1, Code = "SUP", Name = "Supplier" });
        await context.SaveChangesAsync();
    }

    private static async Task SeedInventoryMasterDataAsync(ApplicationDbContext context)
    {
        context.UnitOfMeasures.Add(new UnitOfMeasure { Id = 1, Code = "PCS", Name = "Pieces" });
        context.Products.Add(new Product
        {
            Id = 1,
            Code = "ITEM-01",
            Name = "Item",
            BaseUomId = 1
        });
        context.Warehouses.Add(new Warehouse { Id = 1, Code = "WH", Name = "Warehouse" });
        context.Zones.Add(new Zone { Id = 1, WarehouseId = 1, Code = "Z", Name = "Zone" });
        context.Locations.Add(new Location { Id = 1, ZoneId = 1, Code = "L", Name = "Location" });
        await context.SaveChangesAsync();
    }
}
