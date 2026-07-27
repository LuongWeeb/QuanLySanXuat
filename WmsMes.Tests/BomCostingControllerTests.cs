using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.ViewModels;

namespace WmsMes.Tests;

public class BomCostingControllerTests
{
    [Fact]
    public async Task Create_AveragesOnlyPositiveLotPricesForMaterialCost()
    {
        await using var context = Context();
        var parent = Product("FG", ProductType.FinishedGood);
        var component = Product("RM", ProductType.RawMaterial, standardCost: 99m);
        context.Products.AddRange(parent, component);
        context.Lots.AddRange(
            Lot(component, "RM-01", qty: 5m, unitPrice: 10m),
            Lot(component, "RM-02", qty: 1m, unitPrice: 20m),
            Lot(component, "RM-EMPTY", qty: 0m, unitPrice: 1_000m));
        await context.SaveChangesAsync();

        await Controller(context).Create(Input(parent, component));

        context.ChangeTracker.Clear();
        var bom = await context.BOMs.SingleAsync();
        Assert.Equal(15m, bom.TotalMaterialCost);
        Assert.Equal(15m, bom.TotalStandardCost);
    }

    [Fact]
    public async Task Create_UsesProductStandardCostWhenComponentHasNoPositiveLots()
    {
        await using var context = Context();
        var parent = Product("FG", ProductType.FinishedGood);
        var component = Product("RM", ProductType.RawMaterial, standardCost: 7.25m);
        context.Products.AddRange(parent, component);
        context.Lots.Add(Lot(component, "RM-EMPTY", qty: 0m, unitPrice: 99m));
        await context.SaveChangesAsync();

        await Controller(context).Create(Input(parent, component, qtyPer: 2m));

        context.ChangeTracker.Clear();
        Assert.Equal(14.50m, (await context.BOMs.SingleAsync()).TotalMaterialCost);
    }

    [Fact]
    public async Task Create_AppliesScrapPercentageToMaterialRequirement()
    {
        await using var context = Context();
        var parent = Product("FG", ProductType.FinishedGood);
        var component = Product("RM", ProductType.RawMaterial);
        context.Products.AddRange(parent, component);
        context.Lots.Add(Lot(component, "RM-01", qty: 1m, unitPrice: 20m));
        await context.SaveChangesAsync();

        await Controller(context).Create(Input(parent, component, qtyPer: 2m, scrapPercent: 10m));

        context.ChangeTracker.Clear();
        Assert.Equal(44m, (await context.BOMs.SingleAsync()).TotalMaterialCost);
    }

    [Fact]
    public async Task Create_UsesOnlyActiveRoutingStepsAndWorkCenterRates()
    {
        await using var context = Context();
        var parent = Product("FG", ProductType.FinishedGood);
        var component = Product("RM", ProductType.RawMaterial);
        var activeCenter = WorkCenter("ACTIVE-WC", laborRate: 60m, machineRate: 30m);
        var inactiveCenter = WorkCenter("INACTIVE-WC", laborRate: 1_000m, machineRate: 1_000m);
        context.Products.AddRange(parent, component);
        context.Routings.AddRange(
            Routing(parent, isActive: true, Step(activeCenter, minutes: 30m)),
            Routing(parent, isActive: false, Step(inactiveCenter, minutes: 60m)));
        await context.SaveChangesAsync();

        await Controller(context).Create(Input(parent, component));

        context.ChangeTracker.Clear();
        Assert.Equal(45m, (await context.BOMs.SingleAsync()).TotalOperationCost);
    }

    [Fact]
    public async Task Create_WhenMultipleActiveRoutingsExist_UsesHighestRoutingId()
    {
        await using var context = Context();
        var parent = Product("FG", ProductType.FinishedGood);
        var component = Product("RM", ProductType.RawMaterial);
        var olderCenter = WorkCenter("OLDER-WC", laborRate: 60m, machineRate: 0m);
        var newerCenter = WorkCenter("NEWER-WC", laborRate: 90m, machineRate: 0m);
        var olderRouting = Routing(parent, isActive: true, Step(olderCenter, minutes: 60m));
        context.Products.AddRange(parent, component);
        context.Routings.Add(olderRouting);
        await context.SaveChangesAsync();
        var newerRouting = Routing(parent, isActive: true, Step(newerCenter, minutes: 60m));
        context.Routings.Add(newerRouting);
        await context.SaveChangesAsync();
        Assert.True(newerRouting.Id > olderRouting.Id);

        await Controller(context).Create(Input(parent, component));

        context.ChangeTracker.Clear();
        Assert.Equal(90m, (await context.BOMs.SingleAsync()).TotalOperationCost);
    }

    [Fact]
    public async Task Create_RoundsEachCostAwayFromZeroBeforeSavingTotal()
    {
        await using var context = Context();
        var parent = Product("FG", ProductType.FinishedGood);
        var component = Product("RM", ProductType.RawMaterial, standardCost: 1m);
        var center = WorkCenter("WC", laborRate: 0.30m, machineRate: 0m);
        context.Products.AddRange(parent, component);
        context.Routings.Add(Routing(parent, isActive: true, Step(center, minutes: 1m)));
        await context.SaveChangesAsync();

        await Controller(context).Create(Input(parent, component, qtyPer: 1.005m));

        context.ChangeTracker.Clear();
        var bom = await context.BOMs.SingleAsync();
        Assert.Equal(1.01m, bom.TotalMaterialCost);
        Assert.Equal(0.01m, bom.TotalOperationCost);
        Assert.Equal(1.02m, bom.TotalStandardCost);
    }

    [Fact]
    public async Task ToggleActive_WhenActivating_RecalculatesAndSavesCurrentCost()
    {
        await using var context = Context();
        var parent = Product("FG", ProductType.FinishedGood);
        var component = Product("RM", ProductType.RawMaterial, standardCost: 10m);
        var bom = Bom(parent, component, qtyPer: 2m);
        bom.TotalMaterialCost = 999m;
        bom.TotalOperationCost = 999m;
        bom.TotalStandardCost = 1_998m;
        context.BOMs.Add(bom);
        await context.SaveChangesAsync();

        await Controller(context).ToggleActive(bom.Id);

        context.ChangeTracker.Clear();
        var persisted = await context.BOMs.SingleAsync();
        Assert.True(persisted.IsActive);
        Assert.Equal(20m, persisted.TotalMaterialCost);
        Assert.Equal(0m, persisted.TotalOperationCost);
        Assert.Equal(20m, persisted.TotalStandardCost);
    }

    [Fact]
    public async Task ToggleActive_WhenDeactivating_DoesNotRecalculateCost()
    {
        await using var context = Context();
        var parent = Product("FG", ProductType.FinishedGood);
        var component = Product("RM", ProductType.RawMaterial, standardCost: 10m);
        var bom = Bom(parent, component);
        bom.IsActive = true;
        bom.TotalMaterialCost = 88m;
        bom.TotalOperationCost = 12m;
        bom.TotalStandardCost = 100m;
        context.BOMs.Add(bom);
        await context.SaveChangesAsync();

        await Controller(context).ToggleActive(bom.Id);

        context.ChangeTracker.Clear();
        var persisted = await context.BOMs.SingleAsync();
        Assert.False(persisted.IsActive);
        Assert.Equal(88m, persisted.TotalMaterialCost);
        Assert.Equal(12m, persisted.TotalOperationCost);
        Assert.Equal(100m, persisted.TotalStandardCost);
    }

    private static ApplicationDbContext Context() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"BOM_Costing_{Guid.NewGuid()}")
            .Options);

    private static BomController Controller(ApplicationDbContext context)
    {
        var controller = new BomController(context)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.TempData = new TempDataDictionary(
            controller.HttpContext,
            Mock.Of<ITempDataProvider>());
        return controller;
    }

    private static BomCreateInputModel Input(
        Product parent,
        Product component,
        decimal qtyPer = 1m,
        decimal scrapPercent = 0m) =>
        new()
        {
            ProductId = parent.Id,
            Version = "V1",
            EffectiveDate = new DateTime(2026, 8, 1),
            Items =
            [
                new BomItemInputModel
                {
                    ComponentProductId = component.Id,
                    QtyPer = qtyPer,
                    ScrapPercent = scrapPercent
                }
            ]
        };

    private static Product Product(
        string code,
        ProductType type,
        decimal standardCost = 0m) =>
        new()
        {
            Code = code,
            Name = code,
            Type = type,
            StandardCost = standardCost,
            IsManufactured = type is ProductType.FinishedGood or ProductType.WIP,
            IsActive = true
        };

    private static Lot Lot(Product product, string lotNo, decimal qty, decimal unitPrice) =>
        new()
        {
            Product = product,
            LotNo = lotNo,
            Qty = qty,
            UnitPrice = unitPrice
        };

    private static WorkCenter WorkCenter(
        string code,
        decimal laborRate,
        decimal machineRate) =>
        new()
        {
            Code = code,
            Name = code,
            HourlyLaborRate = laborRate,
            HourlyMachineRate = machineRate
        };

    private static RoutingStep Step(WorkCenter workCenter, decimal minutes) =>
        new()
        {
            StepNumber = 10,
            StepName = "Operation",
            WorkCenter = workCenter,
            StandardTimeMinutes = minutes
        };

    private static Routing Routing(
        Product product,
        bool isActive,
        params RoutingStep[] steps) =>
        new()
        {
            Product = product,
            Name = $"{product.Code} routing",
            Version = "V1",
            IsActive = isActive,
            Steps = steps
        };

    private static BOM Bom(
        Product parent,
        Product component,
        decimal qtyPer = 1m) =>
        new()
        {
            Product = parent,
            Version = "V1",
            EffectiveDate = new DateTime(2026, 8, 1),
            IsActive = false,
            Items =
            [
                new BOMItem
                {
                    ComponentProduct = component,
                    QtyPer = qtyPer
                }
            ]
        };
}
