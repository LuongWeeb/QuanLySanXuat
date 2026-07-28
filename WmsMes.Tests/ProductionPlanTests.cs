using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.DTOs;
using Xunit;

namespace WmsMes.Tests;

public class ProductionPlanTests
{
    [Fact]
    public async Task CalculatePlanRequirements_AggregatesSharedComponentsAndSubtractsStock()
    {
        await using var context = CreateInMemoryContext();
        await SeedProductsAsync(context);
        context.BOMs.AddRange(
            new BOM
            {
                ProductId = 2,
                Version = "A1",
                IsActive = true,
                Items = { new BOMItem { ComponentProductId = 1, QtyPer = 2.5m } }
            },
            new BOM
            {
                ProductId = 3,
                Version = "B1",
                IsActive = true,
                Items = { new BOMItem { ComponentProductId = 1, QtyPer = 4m, ScrapPercent = 10m } }
            });
        context.StockBalances.Add(new StockBalance
        {
            ProductId = 1,
            LotId = 1,
            LocationId = 1,
            QtyAvailable = 5m
        });
        context.ProductionPlans.Add(new ProductionPlan
        {
            Id = 1,
            PlanNo = "PP-100",
            Items =
            {
                new ProductionPlanItem { ProductId = 2, PlannedQty = 10m },
                new ProductionPlanItem { ProductId = 3, PlannedQty = 3m }
            }
        });
        await context.SaveChangesAsync();

        var service = CreateService(context);
        var results = (await InvokeAsync<IEnumerable<MrpResultDto>>(
            service,
            "CalculatePlanRequirementsAsync",
            1)).ToList();

        var result = Assert.Single(results);
        Assert.Equal(38.2m, result.GrossDemand);
        Assert.Equal(5m, result.StockAvailable);
        Assert.Equal(33.2m, result.NetDemand);
        Assert.Equal("RAW-01", result.ComponentCode);
    }

    [Fact]
    public async Task GenerateWorkOrders_CreatesDraftOrderForEachUnlinkedPlanItem()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedProductsAsync(context);
        context.BOMs.AddRange(
            new BOM { ProductId = 2, Version = "A1", IsActive = true },
            new BOM { ProductId = 3, Version = "B1", IsActive = true });
        context.Routings.AddRange(
            new Routing { ProductId = 2, Name = "A", Version = "RA", IsActive = true },
            new Routing { ProductId = 3, Name = "B", Version = "RB", IsActive = true });
        var planDate = new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc);
        context.ProductionPlans.Add(new ProductionPlan
        {
            Id = 1,
            PlanNo = "PP-200",
            PlanDate = planDate,
            Items =
            {
                new ProductionPlanItem { ProductId = 2, PlannedQty = 10m },
                new ProductionPlanItem { ProductId = 3, PlannedQty = 4m }
            }
        });
        await context.SaveChangesAsync();

        var generated = await InvokeAsync<bool>(
            CreateService(context),
            "GenerateWorkOrdersAsync",
            1,
            "planner");

        Assert.True(generated);
        var orders = await context.WorkOrders.OrderBy(order => order.ProductId).ToListAsync();
        Assert.Equal(2, orders.Count);
        Assert.All(orders, order => Assert.Equal(WorkOrderStatus.Draft, order.Status));
        Assert.Equal(new[] { "WO-PP-200-PROD-A", "WO-PP-200-PROD-B" }, orders.Select(order => order.Code));
        Assert.All(orders, order => Assert.Equal(planDate.AddDays(7), order.DueDate));
        Assert.All(
            await context.ProductionPlanItems.ToListAsync(),
            item => Assert.NotNull(item.WorkOrderId));
    }

    [Fact]
    public async Task CompletePlan_TransitionsDraftPlanOnlyOnce()
    {
        await using var context = CreateInMemoryContext();
        context.ProductionPlans.Add(new ProductionPlan { Id = 1, PlanNo = "PP-300" });
        await context.SaveChangesAsync();
        var service = CreateService(context);

        Assert.True(await InvokeAsync<bool>(service, "CompletePlanAsync", 1));
        Assert.False(await InvokeAsync<bool>(service, "CompletePlanAsync", 1));
        Assert.Equal(DocumentStatus.Completed, (await context.ProductionPlans.FindAsync(1))!.Status);
    }

    private static ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static object CreateService(ApplicationDbContext context)
    {
        var serviceType = typeof(ApplicationDbContext).Assembly.GetType(
            "WmsMes.Web.Services.ProductionPlanService");
        Assert.NotNull(serviceType);

        return Activator.CreateInstance(serviceType!, context)!;
    }

    private static async Task<T> InvokeAsync<T>(object target, string methodName, params object[] arguments)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task<T>>(method!.Invoke(target, arguments));
        return await task;
    }

    private static async Task SeedProductsAsync(ApplicationDbContext context)
    {
        context.UnitOfMeasures.Add(new UnitOfMeasure { Id = 1, Code = "PCS", Name = "Pieces" });
        context.Products.AddRange(
            new Product { Id = 1, Code = "RAW-01", Name = "Raw", BaseUomId = 1 },
            new Product { Id = 2, Code = "PROD-A", Name = "Product A", BaseUomId = 1, IsManufactured = true },
            new Product { Id = 3, Code = "PROD-B", Name = "Product B", BaseUomId = 1, IsManufactured = true });
        context.Warehouses.Add(new Warehouse { Id = 1, Code = "WH", Name = "Warehouse" });
        context.Zones.Add(new Zone { Id = 1, WarehouseId = 1, Code = "ZONE", Name = "Zone" });
        context.Locations.Add(new Location { Id = 1, ZoneId = 1, Code = "LOC", Name = "Location" });
        context.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "RAW-LOT", Qty = 100m });
        await context.SaveChangesAsync();
    }
}
